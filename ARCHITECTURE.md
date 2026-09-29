# Архітектура

Які підсистеми є в прототипі, за що кожна відповідає і як вони взаємодіють.

## 1. Загальна схема

```
DroneInput ──► DroneController ──► Rigidbody (сили й моменти) ──► Transform
 (стіки)        (PID, тяга, драг)                                     │
                                                                      ├──► CinemachineCamera (Chase / FPV)
                                                                      ├──► DroneHud, PropellerSpinner
                                                                      └──► TerrainStreamer ──► тайли Terrain
                                                                            FloatingOrigin ──► зсув світу
```

Головний принцип: **ввід нічого не рухає напряму**, він лише задає бажання (газ, тангаж, крен, рискання). Рух виникає тільки із сил і моментів, прикладених до `Rigidbody`; швидкість ніде не задається.

## 2. Структура проєкту

```
Assets/_Project/
  Configs/      DroneConfig.asset, WorldSettings.asset
  Materials/    TerrainMat (URP/Terrain/Lit), TreeTrunk, TreeCrown, Red
  Prefabs/      Drone, Three (дерево з LODGroup)
  Scenes/       SampleScene
  Scripts/      FlyDrone.asmdef (Unity.InputSystem, Unity.TextMeshPro, Unity.Cinemachine)
    Drone/      DroneController, DroneInput, DroneConfig, PidController, PropellerSpinner
    World/      TerrainStreamer, TileBuilder, TerrainField, PerlinNoise,
                TerrainLayerFactory, WorldSettings, FloatingOrigin, TestFlightField
    CameraRig/  CameraSwitcher
    UI/         DroneHud
    Tools/      FlightBenchmark
```

Namespace-и: `FlyDrone.Drone`, `FlyDrone.World`, `FlyDrone.CameraRig`, `FlyDrone.UI`, `FlyDrone.Tools`.

| Скрипт | Відповідальність |
|---|---|
| `DroneInput` | Читає Input System (клавіатура / геймпад), віддає нормалізовані осі. Про фізику нічого не знає. |
| `DroneController` | «Польотний контролер»: ввід + стан дрона → сила тяги й момент. Тут же опір повітря. |
| `PidController` | Універсальний регулятор: P, I, D, обмеження інтегралу, D по виміру. |
| `DroneConfig` | ScriptableObject з усіма параметрами польоту; змінюється без перекомпіляції. |
| `PropellerSpinner` | Візуальне обертання пропелерів від газу. |
| `TerrainField` | Функція «координати в метрах → висота 0..1». |
| `TileBuilder` | Чиста функція без Unity API: будує дані тайла у фоновому потоці. |
| `TerrainStreamer` | Вирішує, які тайли потрібні, замовляє збірку, застосовує результат, керує пулом. |
| `TerrainLayerFactory` | Процедурні текстури шарів terrain. |
| `WorldSettings` | ScriptableObject з параметрами світу й рендера terrain. |
| `FloatingOrigin` | Зсуває світ, щоб дрон не відлітав далеко від (0, 0, 0). |
| `CameraSwitcher` | Перемикає Chase / FPV. |
| `DroneHud` | Телеметрія на екрані. |
| `FlightBenchmark` | Відтворюваний проліт для замірів (див. [BENCHMARK.md](BENCHMARK.md)). |

## 3. Ієрархія сцени

```
Drone            layer Drone; Rigidbody, BoxCollider (0.25, 0.06, 0.25),
                 DroneInput, DroneController, PropellerSpinner
├── Body         Cube (0.25, 0.06, 0.25), без колайдера
├── Nose         червоний Cube, Pos (0, 0, 0.14) — показує «перед»
├── FpvMount     Pos (0, 0.04, 0.12), Rot X = −20 (uptilt FPV-камери)
└── Prop_FL/FR/BL/BR
SpawnPoint       точка респауну; стрімер опускає її на 2 м над рельєфом
World            TerrainStreamer; тайли — дочірні об'єкти
Systems          FloatingOrigin, FlightBenchmark
CameraRig        CameraSwitcher
CM_Chase         CinemachineCamera: Follow (Lazy Follow, offset 0, 1.2, −3.5) + Rotation Composer
CM_FPV           CinemachineCamera: target FpvMount, Hard Lock + Rotate With Follow Target, FOV 100
Main Camera      CinemachineBrain, Default Blend = Cut
Canvas           DroneHud + HudText (TMP)
Ground, TestField  тестовий полігон першого дня, вимкнені
```

## 4. Фізика польоту (`DroneController`)

### 4.1 Rigidbody

Один `Rigidbody` на кореневому об'єкті. Уся фізика — у `FixedUpdate` (Fixed Timestep = 0.01, 100 Гц), кнопки — в `Update`. Налаштування виставляються в `Awake`:

- `Interpolation = Interpolate` — плавна картинка між фізичними кроками;
- `Collision Detection = Continuous Dynamic` — на швидкості дрон не пролітає крізь terrain;
- `maxAngularVelocity = 20`;
- `linearDamping = 0` — опір рахується вручну.

Орієнтація для розрахунків береться з `_rb.rotation`: з інтерполяцією `transform` у `FixedUpdate` не збігається з фізичним станом.

### 4.2 Тяга

Одна сумарна сила вздовж локальної осі Y (окремі мотори не моделюються — моменти задає контролер). `MaxThrust = m · g · TWR`.

- **Stabilized:** стік газу задає цільову вертикальну швидкість (±5 м/с), PI-регулятор підбирає тягу; тяга ділиться на cos(нахилу), щоб дрон не просідав у поворотах.
- **Acro:** центр стіка — газ висіння (`HoverThrottle = 1 / TWR`), верх — 100 %, низ — 0 %.

### 4.3 Інерція

Окремого коду не потрібно: її дає сам `Rigidbody` (маса, швидкість). Дрон не зупиняється миттєво, а ковзає й розгойдується; кутове прискорення контролера обмежене `maxAngularAccel`.

### 4.4 Опір повітря

Квадратичний і анізотропний, окремо по кожній локальній осі:

```
F = −½ · ρ · Cd · A · v · |v|        DragK = ½ · ρ · Cd · A
```

Площа зверху (0.06 м²) більша, ніж спереду (0.03 м²), тож вертикально дрон гальмує сильніше. Так виникає природна максимальна швидкість.

Перевірка розрахунком: гранична швидкість вільного падіння `√(m·g / DragK.y) ≈ 16 м/с` (≈ 59 км/год); максимальна горизонтальна — ~60–75 км/год.

### 4.5 Стабілізація (PID)

Момент прикладається через `AddRelativeTorque(..., ForceMode.Acceleration)`, тобто без урахування тензора інерції — PID не залежить від розміру колайдера.

- **Stabilized:** цільова орієнтація = поточний курс + нахил від стіка (до 35°). Помилка — кватерніон `target * Inverse(current)` → вісь-кут → локальні осі. PD з похідною по виміру (кутова швидкість), без «удару» при різкій зміні стіка.
- **Acro:** стік задає кутову швидкість (до 360°/с), P-регулятор.
- **Yaw:** в обох режимах — кутова швидкість (до 180°/с).

Знаки осей: +X локально — ніс униз (рух уперед), −Z — крен управо.

PID-коефіцієнти синхронізуються з конфігом щокроку, тож їх можна тюнити в Play Mode.

### 4.6 `DroneConfig` — значення за замовчуванням

| Параметр | Значення |
|---|---|
| mass | 1.2 кг |
| thrustToWeight | 2.5 (висіння на 40 % газу) |
| airDensity | 1.225 кг/м³ |
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

### 4.7 Публічне API

`Mode`, `Config` (get/set), `Throttle01`, `LastThrustForce`, `LastDragForce`, `SetMode()`, `ResetDrone()` (респаун у `spawnPoint`, якщо заданий).

## 5. Ввід (`DroneInput`)

- Дії створюються в коді, без `.inputactions`-ассета.
- `[DefaultExecutionOrder(-100)]`, щоб прапорці `WasPressedThisFrame` читалися рівно один раз.
- Deadzone 0.05, expo 0.3: `(1 − e)·x + e·x³` — точніше керування біля центру стіка.
- Властивості: `Attitude` (x — roll, y — pitch), `Vertical`, `Yaw`, `ToggleModePressed`, `ToggleCameraPressed`, `ResetPressed`.
- `SetOverride(attitude, vertical, yaw)` / `ClearOverride()` — програмна підміна стіків для автопілота й тестів.

## 6. Світ і стрімінг

Один величезний Terrain дорогий і за пам'яттю, і за рендером, тому світ 8×8 км складається з тайлів 1×1 км, які підвантажуються навколо дрона.

### 6.1 Генерація рельєфу (`TerrainField`, `PerlinNoise`)

- fBm-пагорби (`hills² · 0.4`) + ridged-хребти (`0.2 + ridges · 0.8`), низькочастотна маска гір, domain warping 250 м, базова частота 1/1500.
- `Forest01` задає плями лісу.
- Власний `PerlinNoise`: детермінований за seed, потокобезпечний, з параметром `period` для текстур, що тайляться.

### 6.2 Збірка тайла (`TileBuilder`)

Чиста функція без Unity API, запускається в `Task.Run`. Приймає незмінний знімок `TileBuildParams`, повертає `TileData` (Heights `[z,x]`, Alphamaps `[z,x,layer]`, Trees).

- **Шви між тайлами гарантовані:** світова координата вершини = `(baseX + x) · step`, де `baseX = coord.X · (res − 1)` — сусідні тайли рахують крайні вершини в тих самих точках.
- **Шари:** 0 Grass, 1 Rock (схил > 27°), 2 Sand (низини), 3 Snow (> 70 % висоти). 4 шари = один прохід URP Terrain Lit. Текстури 128×128 генерує `TerrainLayerFactory`.
- **Дерева:** jittered grid 14 м із seed на тайл; не ростуть на крутих схилах, у снігу й на піску.

### 6.3 Стрімінг (`TerrainStreamer`)

- Навколо дрона — квадрат 5×5 тайлів (`loadRadius` 2), вивантаження за `unloadRadius` 3: гістерезис, щоб тайли не «блимали» на межі.
- Тайли замовляються від найближчих, не більше `maxConcurrentJobs` фонових задач одночасно.
- Застосування (`SetHeights`, alphamaps, дерева) — не більше одного тайла на кадр (time slicing).
- `Terrain`-об'єкти перевикористовуються з пулу (`poolSize` 8); `TerrainData` явно знищується при вивантаженні.
- Після змін — `Terrain.SetConnectivityDirty()` (`allowAutoConnect`, `groupingID = 0`), щоб LOD сусідів узгоджувався.
- При старті найближчі 3×3 тайли будуються синхронно, потім `PlaceAtSpawn()` ставить точку респауну над рельєфом.
- Координати тайлів рахуються в локальному просторі стрімера — сумісно з floating origin. Світ центрований: тайли від −4 до 3.
- API: `TryGetHeight(worldPos, out height)`, `ActiveTiles`, `PendingTiles`, `LastBuildMs`, `LastApplyMs`. ProfilerMarker-и `TerrainStreamer.Apply`, `TerrainStreamer.BuildOnMainThread`.

### 6.4 `WorldSettings` — значення за замовчуванням

tileSize 1000, worldSizeTiles 8, maxHeight 500, heightmap 257, alphamap 256, seed 1337, sand 0.06, snow 0.7, rockSlope 27°, treeCell 14 м, treeDensity 0.6, treeMaxSlope 25°, loadRadius 2, unloadRadius 3, maxConcurrentJobs 2, poolSize 8, pixelError 6, basemapDistance 300, treeDistance 700, useBackgroundThreads ✓. Параметри рендера terrain і їхній вплив на FPS — у [BENCHMARK.md](BENCHMARK.md).

### 6.5 Дерева

Префаб: Trunk (Cylinder) + Crown (Sphere), без колайдерів, матеріали з GPU Instancing. LODGroup: LOD0 обидва рендерери до ~10 %, LOD1 лише крона до ~1 %, далі Culled.

### 6.6 Floating Origin

Далеко від (0, 0, 0) `float` втрачає точність, і фізика «тремтить». `FloatingOrigin` при відльоті далі 2 км по горизонталі зсуває всі кореневі об'єкти сцени (крім Canvas), викликає `Physics.SyncTransforms()` і `OnTargetObjectWarped` для всіх `CinemachineCamera`. `TotalOffset` дає справжню позицію у світі. Працює в `LateUpdate`, `[DefaultExecutionOrder(1000)]`.

## 7. Камери, HUD, візуал

- **Камери:** `CM_Chase` і `CM_FPV`, Far Clip Plane 2500; `CameraSwitcher` вмикає одну з масиву. Туман Linear 800–2200 м ховає межу завантаженої зони.
- **DroneHud:** телеметрія 10 разів на секунду (`<mspace>`): MODE, SPD, V/S, AGL (raycast без шару Drone), THR, PIT, ROL, FPS; зі стрімером — ще TILE, BLD, APL і POS у км.
- **PropellerSpinner:** оберти від `Throttle01` — idle 120, max 600 об/хв (візуальні, без стробоскопічного ефекту), плавний розгін `spoolRate` 8. FL + BR обертаються за годинниковою, FR + BL — проти.

## 8. Обмеження й наступні кроки

- Автотестів поки немає. Заплановано: EditMode (PID, формули конфігу, межі висот, детермінізм, шви тайлів, сума ваг splat = 1) і PlayMode (висіння, вихід на maxTilt, гранична швидкість в Acro при газі 0).
- Генерація через `Task.Run`; наступний крок — Jobs + Burst і пул масивів тайлів, щоб прибрати алокації.
- `Apply` тайла ще можна розбити на кілька кадрів (висоти → текстури → дерева).
- Немає трави (detail objects) і білбордів дерев на відстані.
- Floating origin може дати стрибок інтерполяції на один кадр у момент зсуву.
- Розширення фізики: вітер і турбулентність, диференційні оберти пропелерів при yaw.
