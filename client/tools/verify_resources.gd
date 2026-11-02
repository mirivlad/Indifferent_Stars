extends SceneTree

const STAMP = "res://.godot/client_resources.sha256"
const IMPORTS = {
	"res://assets/models/asteroids.glb": "PackedScene",
	"res://assets/models/ship_player.glb": "PackedScene",
	"res://assets/models/station_kestrel.glb": "PackedScene",
	"res://assets/music/A Speck Among Stars.mp3": "AudioStream",
	"res://assets/music/The Endless Silence.mp3": "AudioStream",
	"res://icon.svg": "Texture2D",
}

func _initialize() -> void:
	var fingerprints: PackedStringArray = [
		Engine.get_version_info().string,
		FileAccess.get_sha256("res://project.godot"),
		FileAccess.get_sha256("res://.godot/mono/temp/bin/Debug/IndifferentStars.Client.dll"),
	]
	for source: String in IMPORTS:
		var config := ConfigFile.new()
		if config.load(source + ".import") != OK:
			fail("Missing import configuration: " + source)
			return
		var target: String = config.get_value("remap", "path", "")
		var checksums_path := target.get_basename() + ".md5"
		if target.is_empty() or not FileAccess.file_exists(target) or not FileAccess.file_exists(checksums_path):
			fail("Missing imported resource: " + source)
			return
		var checksums := FileAccess.get_file_as_string(checksums_path)
		var source_md5 := FileAccess.get_md5(source)
		var target_md5 := FileAccess.get_md5(target)
		if source_md5.is_empty() or not checksums.contains('source_md5="%s"' % source_md5):
			fail("Source changed since import: " + source)
			return
		if target_md5.is_empty() or not checksums.contains('dest_md5="%s"' % target_md5):
			fail("Imported resource checksum mismatch: " + source)
			return
		var resource := ResourceLoader.load(source)
		if resource == null or not resource.is_class(IMPORTS[source]):
			fail("Cannot load imported resource: " + source)
			return
		fingerprints.append(source + ":" + source_md5 + ":" + target_md5 + ":" + FileAccess.get_sha256(source + ".import"))
	var game_script := ResourceLoader.load("res://scripts/GameMain.cs") as Script
	var main_scene := ResourceLoader.load("res://scenes/Main.tscn") as PackedScene
	if game_script == null or not game_script.can_instantiate() or main_scene == null or not main_scene.can_instantiate():
		fail("Cannot load the compiled C# client and main scene.")
		return
	var fingerprint := "\n".join(fingerprints).sha256_text()
	if OS.get_cmdline_user_args().has("--record"):
		var stamp := FileAccess.open(STAMP, FileAccess.WRITE)
		if stamp == null:
			fail("Cannot record the verified resource fingerprint.")
			return
		stamp.store_string(fingerprint)
		stamp.close()
	elif not FileAccess.file_exists(STAMP) or FileAccess.get_file_as_string(STAMP) != fingerprint:
		fail("Resource import configuration has not been verified yet.")
		return
	print("Client resources verified: models, music, icon, C# client and main scene.")
	quit(0)

func fail(message: String) -> void:
	printerr(message)
	quit(1)
