using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Server;

public sealed partial class UdpGameServer
{
    private readonly WorldSaveStore? _save;
    private readonly Dictionary<string, SavedProfile> _profiles = new(StringComparer.Ordinal);
    private readonly byte[] _challengeKey = RandomNumberGenerator.GetBytes(32);
    private double _lastSaveSeconds;

    private void LoadWorld()
    {
        var stored = _save?.Load(_catalog);
        if (stored is null) return;
        var ids = new HashSet<uint>();
        foreach (var (key, profile) in stored.Profiles)
        {
            var player = profile.Player;
            var ship = player.Ship;
            if (key.Length != 64 || !key.All(Uri.IsHexDigit) || !ship.IsPlayer || ship.Id < 10000 ||
                ship.Id >= stored.Simulation.NextPlayerId || !ids.Add(ship.Id) || !HullCatalog.Contains(ship.HullId) || !ship.Position.IsFinite ||
                !ship.Velocity.IsFinite || player.Credits < 0 || !double.IsFinite(player.CapturedAt) || !_world.ValidHangar(player) ||
                _catalog.Find(player.StationId)?.Kind != BodyKind.Station ||
                ((ship.Docked || ship.DockingGranted) &&
                _catalog.Find((int)ship.DockingStationId)?.Kind != BodyKind.Station))
                throw new InvalidOperationException("Invalid persisted player.");
            _profiles.Add(key, profile);
        }
        _world.RestoreSimulation(stored.Simulation);
    }

    private void SaveWorld()
    {
        if (_save is null) return;
        var snapshot = new Dictionary<string, SavedProfile>(_profiles, StringComparer.Ordinal);
        foreach (var session in _sessions.Values)
            if (session.ProfileKey is { } key) snapshot[key] = CaptureProfile(session);
        _save.Write(_world, snapshot);
        _lastSaveSeconds = _clock.Elapsed.TotalSeconds;
    }

    private SavedProfile CaptureProfile(PlayerSession session) =>
        new(_world.CapturePlayer(session.PlayerId), session.ClientId, session.LastTradeResult, session.LastShipyardResult);

    private string? AuthenticateProfile(HelloMessage hello, IPEndPoint endpoint, double now)
    {
        if (hello.ProfileKey.Length == 0 || hello.ClientId == Guid.Empty)
        {
            Send(endpoint, new HelloRejectedMessage("This server requires a persistent client profile."));
            return null;
        }

        Span<byte> exported = stackalloc byte[128];
        if (!Convert.TryFromBase64String(hello.ProfileKey, exported, out var keySize) || keySize != 91)
        {
            Send(endpoint, new HelloRejectedMessage("Invalid player profile key."));
            return null;
        }

        var bucket = (long)(now / 10);
        var challenge = Challenge(hello, endpoint, bucket);
        if (hello.Proof.Length == 0)
        {
            Send(endpoint, new ProfileChallengeMessage(challenge));
            return null;
        }

        if (Proves(challenge) || (bucket > 0 && Proves(Challenge(hello, endpoint, bucket - 1))))
            return Convert.ToHexString(SHA256.HashData(exported[..keySize]));

        Send(endpoint, new ProfileChallengeMessage(challenge));
        return null;

        bool Proves(string cookie)
        {
            if (!hello.Proof.StartsWith(cookie + ".", StringComparison.Ordinal)) return false;
            Span<byte> signature = stackalloc byte[64];
            if (!Convert.TryFromBase64Chars(hello.Proof.AsSpan(cookie.Length + 1), signature, out var signatureSize) ||
                signatureSize != signature.Length) return false;
            try
            {
                using var key = ECDsa.Create();
                key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(hello.ProfileKey), out var consumed);
                return consumed == keySize && key.KeySize == 256 &&
                    key.VerifyData(Encoding.ASCII.GetBytes(cookie), signature, HashAlgorithmName.SHA256);
            }
            catch (CryptographicException) { return false; }
        }
    }

    private string Challenge(HelloMessage hello, IPEndPoint endpoint, long bucket)
    {
        var material = FormattableString.Invariant(
            $"IndifferentStars/{ProtocolConstants.Version}|{hello.ClientId:N}|{hello.ProfileKey}|{endpoint}|{bucket}");
        return Convert.ToHexString(HMACSHA256.HashData(_challengeKey, Encoding.UTF8.GetBytes(material)));
    }
}
