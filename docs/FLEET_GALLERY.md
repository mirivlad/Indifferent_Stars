# Галерея флота (016)

| Корпус | Назначение | Ш × В × Д, м | Масса | Трюм | Обычный ход / форсаж | Сканер | Треугольники |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: |
| Wren | Курьер | 9,24 × 4,73 × 21,59 | 20 т | 40 | 260 / 720 м/с | 200 км | 16 740 |
| Tern | Лёгкий грузовой | 14,06 × 5,48 × 23,69 | 37 т | 86 | 220 / 610 м/с | 170 км | 5 044 |
| Mole | Добытчик | 10,62 × 5,62 × 18,88 | 33 т | 74 | 215 / 580 м/с | 140 км | 3 416 |
| Petrel | Исследователь | 14,02 × 5,70 × 25,90 | 22 т | 33 | 275 / 700 м/с | 300 км | 3 872 |

Tern несёт три грузовые кассеты на открытой раме между двумя
гондолами. Кабина смещена к левому борту. Верх гондол занят радиаторами.
Геометрия включает узлы сцепки, защиты и обслуживающее оснащение.
Курьеры грузового узла используют Tern и берут до 10 единиц груза за рейс.
Wren остаётся стартовым кораблём и берёт 6 единиц за рейс NPC.

| Корпус | Нос | Корма |
| --- | --- | --- |
| Wren | ![Wren со стороны носа](media/fleet/wren-bow.png) | ![Wren со стороны кормы](media/fleet/wren-stern.png) |
| Tern | ![Tern со стороны носа](media/fleet/tern-bow.png) | ![Рама и движители Tern](media/fleet/tern-stern.png) |
| Mole | ![Добычные головки Mole](media/fleet/mole-bow.png) | ![Корма Mole](media/fleet/mole-stern.png) |
| Petrel | ![Сенсор Petrel](media/fleet/petrel-bow.png) | ![Радиаторы и корма Petrel](media/fleet/petrel-stern.png) |

[Внешняя камера в полёте](media/fleet/tern-flight.png) ·
[Кокпит Tern](media/fleet/tern-cockpit.png) ·
[Кокпит Mole](media/fleet/mole-cockpit.png) ·
[Кокпит Petrel](media/fleet/petrel-cockpit.png).

У Mole высоко расположенный мостик между двумя рычагами с защитными
кольцами добычных головок. Petrel несёт открытую сегментированную антенну,
тонкий продольный корпус и боковые радиаторы. Добытчики используют Mole
для извлечения до 10 единиц из реальной залежи за цикл. Исследователи
используют Petrel и отмечают обследованные объекты.

## Воспроизведение

```bash
blender --background --factory-startup --python-exit-code 1 --python tools/art/build_ship_tern.py
blender --background --factory-startup --python-exit-code 1 --python tools/art/build_ship_professions.py -- mole
blender --background --factory-startup --python-exit-code 1 --python tools/art/build_ship_professions.py -- petrel
python3 tools/art/export_collision_meshes.py
python3 tools/art/export_collision_meshes.py --check
blender --background --factory-startup --python-exit-code 1 --python tools/art/check_collision_blender.py
tools/run-server.sh --bind 127.0.0.1 --port 17894 --diagnostic-hull tern
tools/run-client.sh --server 127.0.0.1:17894
IS_FLEET_CAPTURE=/tmp/indifferent-fleet godot-dotnet --path client --resolution 1280x720 res://tests/FleetChecks.tscn
IS_SERVER=127.0.0.1:17894 IS_FLEET_PLAY=1 godot-dotnet --path client --resolution 1280x720 res://tests/FleetChecks.tscn
```

Исходники Tern — `tools/art/build_ship_tern.py`, запечённые UV/PBR 768² в
`tools/art/sources/ship_tern.blend` и итоговом GLB. Независимый импорт Blender
5.2.2 LTS подтвердил совпадение всех 5 044 треугольников с серверной
оболочкой. Godot 4.7.2 .NET запустил галерею и полёт со штатным UDP-сервером.
В двух независимых UDP-сокетах получены данные корпуса в приветствии
и снимке наблюдателя. Тест работы NPC подтвердил загрузку Tern из склада.

При тесте v1 → v2 на копии ранее созданного мира Godot получил тот же
корабль #10000, сохранение стало v2. Для проверки будущих обновлений
отпечатки коллизий в v2 охватывают корпуса, присутствующие в сохранении.

| RX 580, Forward+, 1280×720 | p95 60 кадров | Вызовы отрисовки | VRAM процесса по Godot |
| --- | ---: | ---: | ---: |
| Wren, галерея | 17,40 мс | 9 | 232,7 МиБ |
| Tern, галерея | 30,31 мс | 11 | 262,5 МиБ |
| Tern, полёт у станции | 17,02 мс | 146 | 421,0 МиБ |
| Mole, галерея | 16,89 мс | 10 | 292,1 МиБ |
| Petrel, галерея | 16,86 мс | 9 | 317,9 МиБ |
| Mole, полёт у станции | 17,13 мс | 145 | 420,9 МиБ |
| Petrel, полёт у станции | 17,05 мс | 144 | 416,9 МиБ |

Измерения включают ожидание кадра, драйвер и системную нагрузку.
VRAM относится ко всей сцене и может включать ранее загруженные ресурсы.
Этот короткий замер описывает конкретный запуск.

## Работа NPC и проверки

В 10-минутном автономном прогоне Release, seed 20261101, система 0,
19 NPC выполнили 34 доставки. Два Mole провели четыре добычных цикла и
три доставки, один Petrel завершил пять обследований, шесть Tern
выполнили 17 доставок. На Xeon E5-2680 v4 p95 тика составил 3,175 мс,
p99 — 4,447 мс, максимум — 53,205 мс. Один из 12 000 тиков превысил 50 мс.

Прошли 209 .NET-тестов, независимое сравнение обеих новых оболочек
в Blender, повторный экспорт GLB с идентичным SHA-256 и импорт Godot.
Нативный полёт Mole и Petrel прошёл в F1/F2/F3 с отдельными положениями
глаз пилота. Открыты тяжёлый грузовик, перехватчик, корвет, пиратский
рейдер, фрегат и вторые варианты классов. Слоты и крепления заданы в
данных. Монтаж оборудования и бой относятся к последующим этапам.
