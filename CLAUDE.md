# FlyDrone — опис проєкту

## Мета

Тестове завдання для працевлаштування: Unity-прототип польоту дрона. Вимоги:

- фізика на Rigidbody: тяга, інерція, опір повітря;
- великий terrain з оптимізацією.

Розробка розрахована на 2 дні. День 1 — фізика, керування, камери, HUD. День 2 — terrain, стрімінг, оптимізація, тести, README.

## Стек

- Unity 6 LTS, URP (шаблон Universal 3D)
- Input System (дії створюються в коді, без `.inputactions`-ассета)
- Cinemachine 3.x (namespace `Unity.Cinemachine`, компонент `CinemachineCamera`)
- TextMeshPro (у Unity 6 входить до uGUI)
- Unity Test Framework (EditMode + PlayMode)
- Fixed Timestep = 0.01 (фізика на 100 Гц)

API Unity 6: `Rigidbody.linearVelocity`, `linearDamping`, `angularDamping`. У 2022 LTS це `velocity`, `drag`, `angularDrag`.

## Структура проєкту

```
Assets/_Project/
  Configs/      DroneConfig.asset, WorldSettings.asset
  Materials/    TerrainMat (URP/Terrain/Lit), TreeTrunk, TreeCrown, Red
  Prefabs/      Drone, Tree (з LODGroup)
  Scenes/
  Scripts/      FlyDrone.asmdef (посилання: Unity.InputSystem, Unity.TextMeshPro, Unity.Cinemachine)
    Drone/      DroneController, DroneInput, DroneConfig, PidController, PropellerSpinner
    World/      TerrainStreamer, TileBuilder, TerrainField, PerlinNoise,
                TerrainLayerFactory, WorldSettings, FloatingOrigin, TestFlightField
    CameraRig/  CameraSwitcher
    UI/         DroneHud
    Tools/      FlightBenchmark
  Tests/
    EditMode/   FlyDrone.Tests.EditMode.asmdef, DronePhysicsTests, TerrainGenerationTests
    PlayMode/   FlyDrone.Tests.PlayMode.asmdef, DroneFlightTests
README.md       (корінь репозиторію)
```

Усі скрипти в namespace `FlyDrone.*`: `FlyDrone.Drone`, `FlyDrone.World`, `FlyDrone.CameraRig`, `FlyDrone.UI`, `FlyDrone.Tools`, `FlyDrone.Tests`.

## Ієрархія сцени

```
Drone            layer Drone; Rigidbody, BoxCollider (0.25, 0.06, 0.25),
                 DroneInput, DroneController, PropellerSpinner
├── Body         Cube (0.25, 0.06, 0.25), без колайдера
├── Nose         червоний Cube, Pos (0, 0, 0.14) — показує «перед»
├── FpvMount     порожній, Pos (0, 0.04, 0.12), Rot X = −20 (uptilt FPV-камери)
└── Prop_FL/FR/BL/BR   тонкі кубики-лопаті, Y = 0.045
SpawnPoint       точка респауну; стрімер опускає її на 2 м над рельєфом
World            TerrainStreamer; тайли — дочірні об'єкти
Systems          FloatingOrigin, FlightBenchmark
CameraRig        CameraSwitcher
CM_Chase         CinemachineCamera: Follow (Lazy Follow, offset 0,1.2,−3.5) + Rotation Composer
CM_FPV           CinemachineCamera: target FpvMount, Hard Lock + Rotate With Follow Target, FOV 100
Main Camera      CinemachineBrain, Default Blend = Cut
Canvas           DroneHud + HudText (TMP)
Ground, TestField  тестовий полігон першого дня, вимкнені
```

Обидві CM-камери мають Far Clip Plane = 2500. Туман Linear 800–2200 м.

## Керування

| Дія | Клавіатура | Геймпад |
|---|---|---|
| Тангаж / крен | W S / A D | правий стік |
| Газ | Space / Left Ctrl | лівий стік ↕ |
| Рискання | Q / E | лівий стік ↔ |
| Режим Stabilized ↔ Acro | M | Y / △ |
| Камера | C | X / □ |
| Респаун | R | Start |
| Бенчмарк | B | — |

## Фізична модель (DroneController)

Рух іде лише силами й моментами, швидкість ніде не задається напряму. Уся фізика — у `FixedUpdate`, кнопки — в `Update`. Орієнтація для розрахунків береться з `_rb.rotation`, бо з інтерполяцією `transform` у FixedUpdate не збігається з фізичним станом.

**Тяга** прикладається вздовж локальної осі Y. `MaxThrust = mass · g · thrustToWeight`.

- Stabilized: стік газу задає цільову вертикальну швидкість (±5 м/с), PI-регулятор підбирає тягу, тяга ділиться на cos(нахилу).
- Acro: центр стіка — газ висіння (`HoverThrottle = 1 / TWR`), верх — 100%, низ — 0%.

**Опір повітря** квадратичний і анізотропний: `F = −½·ρ·Cd·A·v·|v|` окремо по локальних осях X, Y, Z. `DragK = ½·ρ·Cd·A`. Вбудований `linearDamping = 0`.

**Орієнтація.** Момент прикладається через `AddRelativeTorque(..., ForceMode.Acceleration)`, тобто без урахування тензора інерції, тож PID не залежить від колайдера.

- Stabilized: цільова орієнтація = поточний курс + нахил від стіка (до 35°). Помилка рахується кватерніоном `target * Inverse(current)` → вісь-кут → локальні осі. PD з похідною по виміру (`UpdateWithRate` з кутовою швидкістю).
- Acro: стік задає кутову швидкість (до 360°/с), P-регулятор.
- Yaw: в обох режимах керується кутова швидкість (до 180°/с).

Знаки осей: +X локально — ніс вниз (рух уперед), −Z — крен вправо.

Налаштування Rigidbody виставляються в `Awake`: Interpolate, ContinuousDynamic, `maxAngularVelocity = 20`.

**Публічне API:** `Mode`, `Config` (get/set — сетер для тестів), `Throttle01` (тяга 0..1), `LastThrustForce`, `LastDragForce`, `SetMode()`, `ResetDrone()` (використовує `spawnPoint`, якщо заданий).

## DroneConfig (ScriptableObject) — значення за замовчуванням

| Параметр | Значення |
|---|---|
| mass | 1.2 кг |
| thrustToWeight | 2.5 (висіння на 40% газу) |
| airDensity | 1.225 |
| dragCoefficient (X, Y, Z) | (1.0, 1.2, 0.9) |
| referenceArea (X, Y, Z) | (0.03, 0.06, 0.03) м² |
| angularDamping | 0.5 |
| maxTiltDeg | 35 |
| maxClimbRate | 5 м/с |
| anglePid | P 60, I 0, D 12 (ωn ≈ 7.7 рад/с, ζ ≈ 0.78) |
| climbPid | P 3, I 0.5, D 0, limit 2 |
| maxRateDeg / ratePid | 360 / P 20 |
| maxYawRateDeg / yawRatePid | 180 / P 8 |
| maxAngularAccel | 80 рад/с² |

PID-коефіцієнти синхронізуються з конфігом щокроку, тож їх можна тюнити в Play Mode.

**Очікувані результати:** вільне падіння ≈ 16 м/с (≈ 59 км/год), максимальна горизонтальна швидкість ~60–75 км/год.

## Ввід (DroneInput)

`[DefaultExecutionOrder(-100)]`, щоб прапорці `WasPressedThisFrame` читалися рівно один раз. Deadzone 0.05, expo 0.3 (`(1−e)·x + e·x³`).

Властивості: `Attitude` (x — roll, y — pitch), `Vertical`, `Yaw`, `ToggleModePressed`, `ToggleCameraPressed`, `ResetPressed`.

`SetOverride(attitude, vertical, yaw)` / `ClearOverride()` — програмна підміна стіків для тестів і автопілота.

## Світ і стрімінг

**TerrainField** — функція «координати в метрах → висота 0..1». Складається з fBm-пагорбів (`hills²·0.4`), ridged-хребтів (`0.2 + ridges·0.8`), низькочастотної маски гір і domain warping 250 м. Частота 1/1500. `Forest01` задає плями лісу. Використовує власний `PerlinNoise` (детермінований за seed, потокобезпечний, з параметром `period` для текстур, що тайляться).

**TileBuilder** — чиста функція без Unity API, запускається в `Task.Run`. Приймає незмінний знімок `TileBuildParams`, повертає `TileData` (Heights [z,x], Alphamaps [z,x,layer], Trees).

- Шви між тайлами гарантовані: світова координата = `(baseX + x) · step`, де `baseX = coord.X · (res − 1)`.
- Шари за індексами: 0 Grass, 1 Rock (схил > 27°), 2 Sand (низини), 3 Snow (> 70% висоти).
- Дерева розставляються jittered grid 14 м із seed на тайл; не ростуть на крутих схилах, у снігу й на піску.

**TerrainLayerFactory** — 4 процедурні текстури 128×128. 4 шари = один прохід URP Terrain Lit.

**TerrainStreamer:**

- Навколо цілі `loadRadius` 2 (квадрат 5×5), вивантаження за `unloadRadius` 3 (гістерезис).
- Замовлення тайлів від найближчих, до `maxConcurrentJobs` фонових задач.
- Застосування не більше одного тайла на кадр (time slicing).
- Пул тайлів (`poolSize` 8); `TerrainData` явно знищується при вивантаженні.
- `Terrain.SetConnectivityDirty()` після змін; `allowAutoConnect`, `groupingID = 0`.
- При старті найближчі 3×3 тайли будуються синхронно, потім `PlaceAtSpawn()`.
- Координати тайлів рахуються в локальному просторі стрімера (сумісно з floating origin).
- Світ центрований: при 8 тайлах координати від −4 до 3.
- ProfilerMarker: `TerrainStreamer.Apply`, `TerrainStreamer.BuildOnMainThread`.
- API: `TryGetHeight(worldPos, out height)`, `ActiveTiles`, `PendingTiles`, `LastBuildMs`, `LastApplyMs`.

**WorldSettings — значення за замовчуванням:** tileSize 1000, worldSizeTiles 8 (8×8 км), maxHeight 500, heightmap 257, alphamap 256, seed 1337, sand 0.06, snow 0.7, rockSlope 27°, treeCell 14 м, treeDensity 0.6, treeMaxSlope 25°, pixelError 6, basemapDistance 300, treeDistance 700, drawInstanced ✓, useBackgroundThreads ✓.

**FloatingOrigin** — при відльоті далі 2 км (по горизонталі) зсуває всі кореневі об'єкти сцени, крім Canvas. Далі викликає `Physics.SyncTransforms()` і `OnTargetObjectWarped` для всіх `CinemachineCamera`. `TotalOffset` дає справжню позицію у світі. Працює в LateUpdate, `[DefaultExecutionOrder(1000)]`.

**Префаб дерева:** Trunk (Cylinder) + Crown (Sphere), без колайдерів, матеріали з GPU Instancing. LODGroup: LOD0 обидва рендерери до ~10%, LOD1 лише крона до ~1%, далі Culled.

## Допоміжне

- **DroneHud** — телеметрія 10 разів на секунду з тегом `<mspace>`: MODE, SPD, V/S, AGL (raycast без шару Drone), THR, PIT, ROL, FPS. Якщо заданий стрімер, додатково TILE, BLD, APL і POS у км.
- **CameraSwitcher** — вмикає одну CM-камеру з масиву, решту вимикає.
- **PropellerSpinner** — оберти від `Throttle01`: idle 120, max 600 об/хв (візуальні, щоб уникнути стробоскопічного ефекту), плавний розгін `spoolRate` 8. Діагональні пари обертаються в протилежні боки: FL+BR за годинниковою, FR+BL проти.
- **FlightBenchmark** — клавіша B: кінематичний проліт 60 с, 35 м/с, 60 м над рельєфом, напрямок (1,0,1). Рахує avg FPS, 1% low, max ms і кількість кадрів > 33 мс. Результат — у Console і в `benchmark.csv` у `Application.persistentDataPath`. Поле `label` підписує прогін.

## Оптимізація: методика

Заміри робляться на білді однаковим бенчмарком. Спершу — «наївна» конфігурація, потім оптимізації вмикаються групами.

| Група | «До» | «Після» |
|---|---|---|
| threads | генерація в головному потоці, heightmap 513 | фонові потоки, heightmap 257 |
| terrain | Pixel Error 1, Basemap 1000, без Draw Instanced | 6, 300, Draw Instanced ✓ |
| trees-fog | Tree Distance 2000, без GPU Instancing, Far Clip 5000, без туману | 700, instancing ✓, 2500, туман 800–2200 |
| shadows | Max Distance 500 | 150 |

## Тести

**EditMode (14):**

- PID: P, обмеження інтегралу, D по виміру, Reset.
- Конфіг: HoverThrottle, MaxThrust, DragK.
- Terrain: висота в діапазоні 0..1, детермінізм за seed, шви по X і по Z (на від'ємних координатах), сума ваг splat = 1, дерева в межах тайла, повторна збірка ідентична.

**PlayMode (3):**

- висіння в Stabilized (дрейф < 0.5 м за 5 с);
- вихід на maxTilt ±3° за 2 с;
- гранична швидкість в Acro при газі 0 = `√(m·g / DragK.y)` ±5%.

Дрон у тестах створюється в неактивному GameObject, отримує конфіг і лише потім активується (щоб `Awake` вже бачив конфіг).

## Стан і відомі обмеження

- Написано туторіали на День 1 і День 2, код не компілювався в живому редакторі Unity. Найімовірніші місця для помилок: назви збірок в `.asmdef`, Cinemachine API у FloatingOrigin.
- Немає трави (detail objects) і білбордів дерев.
- Генерація через `Task.Run`, а не Jobs + Burst; масиви тайлів не пулються.
- `Apply` тайла ще можна розбити на кілька кадрів (висоти → текстури → дерева).
- Floating origin може дати стрибок інтерполяції на один кадр.
- Кандидати на розширення: вітер і турбулентність, диференційні оберти пропелерів при yaw.

## Порядок урізання за браком часу

1. дерева;
2. PlayMode-тести;
3. floating origin;
4. покрокові заміри (лишити before/after).

Фонові потоки, заміри «до/після» і README не ріжуться.
