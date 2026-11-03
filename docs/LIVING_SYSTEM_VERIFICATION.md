# Живая процедурная система — проверки этапов

## 1. Каталог и небесные тела

Генератор v2, протокол v18. Сид 20261101, система 0: 9 планет,
10 спутников и звезда; другие сиды дают 5–9 планет, другие имена,
физические параметры/поверхности и спутники. Землеподобный тип включён.
Фиксированный PRNG и независимые домены; галактическая схема 48–96 систем
со связным каркасом и дополнительными вратами/червоточинами. Схема связей
не означает реализованный полёт между системами.

```bash
dotnet build client/IndifferentStars.Client.csproj --no-restore
dotnet test tests/IndifferentStars.Tests --filter \
 'FullyQualifiedName~SeededSystemTests|FullyQualifiedName~WarpSafetyTests|FullyQualifiedName~ProtocolTests|FullyQualifiedName~GameWorldTests|FullyQualifiedName~ServerIntegrationTests.TwoClientsReconstruct'
godot-dotnet --headless --path client --editor --import --quit
godot-dotnet --path client --resolution 1280x720 res://tests/SeededSystemChecks.tscn
```

Сборка: 0 предупреждений/ошибок. Указанный набор тестов: **59/59**. Импорт и живой Forward+ на RX 580 — PASS:
отрисованы все типы планет, общий вид системы, шейдеры без ошибок.
SeededSystemTests проверяет 32 сида, уникальность id/имён внутри системы,
отсутствие пересечений с родителем, ближайшие поверхности, варп/орбиты,
детерминизм, различия сидов и связность топологии.
UDP-проверка двух клиентов подтверждает одинаковый каталог системы 7
на сиде 81472, включая SystemIndex из HelloAccepted.

![Землеподобная планета](media/seeded-system/terrestrial.png)
![Кратерная поверхность](media/seeded-system/cratered.png)
![Система](media/seeded-system/system_map.png)

Дальнейшие этапы 012: распределённые районы и станции, занятия NPC и события.
Технический успех первого этапа не означает завершение всего задания.
