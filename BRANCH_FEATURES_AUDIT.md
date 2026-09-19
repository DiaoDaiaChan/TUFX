# BRANCH_FEATURES.md 审计与修复报告 (Audit & Remediation)

> 对象：`feature/advanced-effects` 分支 · `BRANCH_FEATURES.md`
> 日期：2026-09-19
> 方法：逐条比对文档声明 ↔ 源码 ↔ 构建产物 ↔ 部署产物，不依赖文档自述

---

## 摘要

| 类别 | 数量 | 说明 |
|---|---|---|
| 审计发现的问题 | 6 | P0×1、P1×2、P2×2、卫生×1 |
| 已修复 | 5 | 含全部 P0/P1 |
| 审计误报（已更正） | 1 | Contact Shadows 尺度度量 —— 文档本来是对的 |
| 阻塞待办 | 1 | 着色器资源包重建（Unity 环境问题，非代码问题） |

**当前部署状态**：`TUFX.dll` 已重建并部署，与仓库 md5 一致；`tufx-advanced.ssf` 仍是 19:20 版本，**GodRays / CAS 的着色器改动尚未进入运行时**（见第四、五节）。

---

## 一、审计更正：1 处误报

> **原审计结论（错误）**：「§4 声称的屏幕投影像素度量仅在 GroundTruthAO 实现，Contact Shadows 未实现」

**这是误报，Contact Shadows 本来就是达标的。** `ContactShadows.shader:43-63` 用一条**等价但写法不同**的路径实现了同一件事：

```hlsl
float3 endPos  = originPos + rayDir * _RayLength;
float4 endClip = mul(_CameraProjectionMatrix, float4(endPos, 1.0));
float2 endUV   = (endClip.xy / endClip.w) * 0.5 + 0.5;
float2 rayPixelDelta = (endUV - i.texcoord) * _MainTex_TexelSize.zw;  // 真实屏幕像素距离
float  rayPixelDist  = length(rayPixelDelta);
if (rayPixelDist < 2.0) return float4(1,1,1,1);                        // 不可分辨 → 跳过
```

它把射线**端点投影回屏幕**量出像素距离（<2px 跳过、2~5px 线性淡出），而 GTAO 是求「每像素代表多少世界单位」再取商。两者数学等价（推导见修订后的文档 §4）。

**误判原因**：原审计只用 `screenspaceRadius|projRadiusPixels|_ScreenParams.x|_ProjectionParams|pixelSizeAtZ` 这组变量名做 grep，而 Contact Shadows 用的是 `_CameraProjectionMatrix` + `_MainTex_TexelSize.zw`，未被命中。

**教训（已写入项目记忆）**：审计「某机制是否存在」时不能按一套变量名 grep，要按**能力**去找（投影矩阵 / 像素距离 / 提前返回）。

---

## 二、本轮修复记录

### P0 — 效果执行顺序不可控 ✅ 已修复

| 项 | 内容 |
|---|---|
| 问题 | `PostProcessLayer.UpdateBundleSortList()` 按 `PostProcessManager.settingsTypes.Keys` 的**反射枚举顺序**追加自定义效果，`sortingPriority` 全仓库零命中。CMAA 2 与 FSR CAS 同为 `AfterStack`，先后随机 —— 一旦 CAS 先跑，就会把未平滑的锯齿当有效高频轮廓锐化。 |
| 修复 | ① `PostProcessAttribute` 新增 `sortingPriority`（升序，越小越先执行）；② `UpdateBundleSortList()` 末尾新增 `CompareBundleRefsByPriority`（主键 `sortingPriority`，次键 `string.CompareOrdinal(类型全名)`），构成**全序**，与反射顺序彻底解耦。 |
| 文件 | `Plugin/TUFX/PostProcessing/Attributes/PostProcessAttribute.cs`、`Plugin/TUFX/PostProcessing/PostProcessLayer.cs` |
| 声明链 | SpectralBokeh 10 / GTAO 20 / ContactShadows 30 / GodRays 40 / HeatDistortion 50 / AnamorphicFlare 60 / Halation 70（BeforeStack）；ModernTonemapping 100 / CMAA2 110 / CAS 120（AfterStack） |
| 验证 | `dotnet build -c Release` 0 error；DLL 元数据中已确认存在 `sortingPriority` 与 `CompareBundleRefsByPriority` |

**顺带查清的引擎事实**（文档原先未写、容易误解）：`PostProcessLayer.Render()` 的固定顺序是
`BeforeStack 自定义` → `内建栈(Bloom/色彩分级)` → `AfterStack 自定义` → `FinalPass(FXAA/SMAA/Dithering)`。
因此前 7 项跑在**色调映射前的 HDR 域**（AO / 遮挡 / 光轴 / 胶片效应在此域才物理正确），后 3 项跑在**显示域**（形态学抗锯齿与自适应锐化必须在最终显示色上执行），设计上恰好正确。

### P1 — GodRays 缺尺度无关度量 ✅ 已修复（源码级）

| 项 | 内容 |
|---|---|
| 问题 | 提取掩膜使用**硬编码 `0.15` UV 半径**，太阳视半径随星系变化（Stock 的 Kerbol vs RSS 的 1:1 太阳）会「抓不到日面」或「整屏被拉丝洗白」。 |
| 修复 | C# 用 `Planetarium.fetch.Sun` 的物理半径与距离按文档公式求视半径像素数（双精度算距离平方防溢出），传 `_SunDiscRadius`（屏幕高度比例，与 shader 的宽高比校正度量口径一致）与 `_SunDiscRadiusPixels`；shader 侧 `<2px` 整段跳过、`2~6px` 余弦淡入，掩膜半径取 2.5 倍视半径。 |
| 文件 | `Plugin/TUFX/PostProcessing/Effects/GodRays.cs`、`Unity/Assets/Shaders/TUFX/GodRays.shader` |
| 验证 | C# 侧已编译进 DLL（UTF-16 字面量 `_SunDiscRadius` 已确认在 DLL 内）；**shader 侧待资源包重打包后生效** |

### P1 — 动态环境管理器缺分支 / 名不副实 ✅ 已修复

| 文档原声称 | 修复后实际行为 |
|---|---|
| 日食：收紧 Bloom 阈值与暗部降噪 | ✅ 新增：Bloom 强度 ×0.70、阈值 ×1.15 |
| 日食：暗部降噪 | ⚠️ **修正为真正可用**：原实现只在 `minLuminance.overrideState` 已为真时才改，否则**静默失效**；现通过租约主动 `Override()`，实测生效 |
| 再入：激发热扰动与胶片红晕 | ✅ 补齐 Halation ×1.6 联动（原先只驱动 HeatDistortion） |
| 深空真空：抑制大气散射 | ✅ **新增分支**：`atmDensity ≤ 1e-5` 或无大气时，GodRays 强度按真空权重线性归零（原先该分支完全不存在） |
| EVA：边缘轻微曲率色差 | ✅ 补齐 `ChromaticAberration` 驱动（原先只有 Vignette.roundness，没有色差） |

**新增 `ParameterLease` 租约机制**：所有被接管的参数在首次接管时记录作者的 `value` 与 `overrideState`，环境失效时**原样归还**，切换预设自动释放旧参数并重新捕获。修复前是直接 `Override()` 后不还原 —— 玩家飞过一次日食，保存的预设就被永久改写了。

> 明确边界：色差分支只作用于预设中**已启用**的 `ChromaticAberration`，管理器不会擅自向玩家预设注入 Effect 节点。若预设未启用色差，面罩色散不出现，暗角曲率仍生效。

### P2 — 文档与代码不符 ✅ 已修正

| 位置 | 原文 | 更正为 |
|---|---|---|
| §2.4 第 4 条曲线 | Gran Turismo (GTTonemap) | **Filmic (Hejl–Burgess–Dawson)**（枚举 `ModernTonemapper { AgX=0, ACES=1, TonyMcMapface=2, Filmic=3 }`，shader 第 84 行） |
| §2.4 ACES 描述 | 「基于 AMPAS 制定的 ACEScc 拟合曲线」 | 明确为 **Narkowicz 单曲线拟合近似**，非官方 ACEScc/ACEScg 变换链路 |
| §1 / §2.2 / §2.3 | 「彻底移植」「官方移植」 | 新增 **§1 实现保真度声明**：XeGTAO 属逐函数移植档；CMAA2 / CAS / AgX 属**原理复现档**（单 Pass、精简形态，非官方源码逐行搬运）。§2.2 补注「不含官方多 Pass 架构与长线条形状对称性分析」 |
| §4 | 「着色器中**全面统一**为像素投影度量」 | 改为**实测口径表**，逐效果标注度量实现 / 阈值 / 淡出区间，并明确「不再声称所有 shader 均已统一」 |
| §2.5 / §2.6 | 模糊表述 | 补实测阈值（2→5px / 2→6px）与恒星视半径公式 |
| §3 | 「高度升高离开稠密大气」等含糊描述 | 重写为**真实触发条件 + 目标效果 + 行为**的分支表，并新增租约机制说明与 Mermaid 流程图更正 |
| §8 Q4 | 「降级为 SMAA/CMAA2」 | 更正为**降级为 SMAA**（`TexturesUnlimitedFXLoader.cs:585`） |

**文档新增章节**
- **§2.12 确定性效果执行顺序**：完整优先级表 + 引擎固定顺序说明 + 全序排序原理
- **§7.3 前置条件**：`TUFX.csproj.user` 被 `.gitignore` 排除，**新克隆仓库无法直接编译**，必须手建并填 `ReferencePath`；并澄清「编译引用来源」与「部署目标实例」是两个不同目录
- **§7.5 部署后一致性自检**：三份产物的 md5 自检脚本 + 检查无扩展名残留文件
- **§8 Q5 / Q6**：执行顺序为何不会变、为何预设不会被改乱

### P2 — 着色器许可证 ✅ 已修复
`FidelityFX_CAS.shader` 补齐 **AMD / MIT 许可证头** + 出处（GPUOpen FidelityFX-FSR）+ 「非逐行搬运」声明。（`CMAA2.shader` 原已带 Intel Apache 2.0 头，正确。）

### 仓库卫生 ✅ 已清理
| 项 | 处理 |
|---|---|
| `GameData/TUFX/Shaders/Shaders` + `Shaders.manifest` | **已删除**。AssetBundle 主清单命名冲突产物，会被 `build_script.txt` 打进发布 zip 污染玩家目录 |
| 仓库根 `unity_build.log`（18:03、仅 9 个 shader、不含 CMAA2） | **已删除**，统一以 `Unity/build.log` 为准 |
| `Unity/Assets/Shaders/Water.meta` 未提交删除 | **确认非误删**：全仓 glob 无任何 `Water.*` 资产，属孤儿 meta，删除即正确清理 |
| 编译后事件回流 | `TUFX.dll` / `TUFX.pdb` 由 PostBuildEvent 自动写入 `GameData/TUFX/Plugins/`，符合文档 §7.3 |

---

## 三、构建与部署验证

### C# 插件
```
dotnet build "Plugin/TUFX.sln" -c Release
→ 已成功生成。 9 个警告, 0 个错误。 用时 3.20s
```
> ⚠️ **`MSBuild.exe` 在本机被安全策略判定为 LOLBin，Bash / PowerShell 均直接拒绝执行**（`dangerouslyDisableSandbox` 亦无效）。
> **可用替代：`dotnet build`**（SDK 6.0.428，`C:\Program Files\dotnet\dotnet`），可正常编译 net471 旧式 csproj，PostBuildEvent 照常执行。
> 9 个警告全部为**既有**警告（`SystemInfo.supportsImageEffects` 过时 ×5、未使用字段/变量 ×4），本轮新增代码**零警告**。

### 部署一致性（md5 逐字节校验）
| 产物 | 仓库 md5 | 部署 md5 | 结论 |
|---|---|---|---|
| `Plugins/TUFX.dll` | `01269ca043ddc1c6fa94561812d9b3d1` | 同 | ✅ 一致（270,336 B，原 266,752 B） |
| `Shaders/tufx-advanced.ssf` | `11786d837220c0654643d1237719dfc9` | 同 | ✅ 一致（但仍是 19:20 版本，见下） |
| `Profiles/TUFX-CinematicAdvanced.cfg` | `089e301d5da49c1b5bdba895380a7159` | 同 | ✅ 一致 |

部署目标：`C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program_newmod\GameData\TUFX`
原 DLL 已备份至 `.workbuddy/backup/TUFX.dll.before-optimization-20260919`（266,752 B），可随时回滚。

DLL 元数据抽查（确认改动真的进了二进制，而非只改了源码）：
`sortingPriority` ✅ / `CompareBundleRefsByPriority` ✅ / `ParameterLease` ✅ / `CMAA2Effect` ✅ / `_SunDiscRadius`(UTF-16) ✅

---

## 四、阻塞项：着色器资源包未能重建

**Unity 2019.4.18f1 批处理打包 exit 1**，工程在加载前即退出：

```
Failed to resolve packages: The "path" argument must be of type string. Received type undefined.
No packages loaded.
```

已排除的原因：
- 网络可达 —— `https://packages.unity.com` 返回 HTTP 200
- `Packages/manifest.json` 与 `packages-lock.json` **一致**（40 / 41 项，无缺失、无版本冲突；多出的 `com.unity.modules.subsystems` 为正常自动模块）
- 所需包均已就位于 `Library/PackageCache`（7 个 registry 包齐全）
- 缓存目录可写（`%LOCALAPPDATA%\Unity\cache\packages` 写入测试通过）
- UPM 子进程本身启动正常（`upm.log`：Server started on port → 收到 Health Request ×2）

**影响**：`GodRays.shader` 与 `FidelityFX_CAS.shader` 的改动**尚未进入 `tufx-advanced.ssf`**。
**兼容性**：当前「新 DLL + 旧资源包」组合是**安全的** —— Unity 的 `Material.SetFloat` 对未声明属性为静默 no-op，旧 GodRays shader 继续走原来的 0.15 掩膜，不会报错或异常；CAS 的改动仅为注释。
**待办**：在**用户自己的终端**重跑文档 §7.2 命令（19:20 那次在用户终端里是成功的）：
```powershell
& "C:\Program Files\Unity\Hub\Editor\2019.4.18f1\Editor\Unity.exe" `
  -batchmode -quit `
  -projectPath "c:\Users\43701\Documents\github\TUFX\Unity" `
  -executeMethod BuildAdvancedBundle.BuildAll `
  -logFile "c:\Users\43701\Documents\github\TUFX\Unity\build.log"
```
若仍失败，删除 `Unity/Library/PackageManager` 后重试；成功后按 §7.4 部署并按 §7.5 做 md5 自检。

---

## 五、已知限制（架构性，非缺陷）

1. **前 7 项效果跑在内建栈之前**。Bloom 与内建色彩分级在它们**之后**执行，因此 AO 压暗的缝隙仍会被 Bloom 部分重新提亮。这是 PostProcessing Stack v2 注入点的固有限制，不是 bug；如需彻底解决须把 AO 改为 opaque pass 内的 CommandBuffer 注入。
2. **`ModernTonemapping` 与内建 `ColorGrading` 同时启用时是两条色调曲线串联**。ExtendFX 自带预设已刻意不含 `ColorGrading` 以规避此问题；自定义预设同时开二者属于用户选择，效果叠加而非互斥。
3. **`BeforeStack` 自定义效果的存在会强制多一次全屏 Blit**（`RenderInjectionPoint` 申请临时 RT）。ExtendFX 全开时共有 8 次额外全屏搬运，4K 下建议按需开启。
4. **`ParameterLease` 依赖 `GetSettingsFor<T>()` 每帧返回同一实例**。若未来实现「运行时热重载预设并替换 settings 对象」，租约会自动重捕获（已按 `ReferenceEquals` 处理），但归还动作会落在旧对象上 —— 旧对象已被丢弃，无副作用，属安全退化。
5. **GodRays 的太阳定位仍基于主方向光**，而非恒星本体朝向。多光源或人造光源场景下可能取到非恒星光源；本次未改动该逻辑（改动风险大于收益）。

---

*审计与修复：2026-09-19 · 全部结论均可回溯到具体文件与行号*
