# TUFX 次世代实验功能套件 (Experimental Features Suite) 技术方案与实现计划

本文件记录了 TUFX 分支关于现代游戏高级视觉特性（AMD FSR 1.0 EASU 渲染缩放、相机物理快门运动模糊、前向兼容半分辨率镜面反射 SSSR、多层物理镜头眩光）的技术评估、CPU/GPU 性能损耗剖析及沙盒化实现计划。所有功能均标定为 **“实验功能 (Experimental / 🧪)”**，确保不影响现有稳定版预设。

---

## 一、 背景与架构目标

在完成了 **Intel XeGTAO（真值环境光遮蔽）**、**Intel CMAA 2（次世代抗锯齿）**、**AMD FidelityFX CAS（动态锐化）** 与 **Blender 4.x AgX 色彩科学** 之后，为进一步缩小 KSP 画面质感与现代顶级 3A 游戏（如虚幻 5、赛博朋克 2077、微软模拟飞行 2024）的世代差距，我们制定了本套实验性特性方案。

### 核心架构原则
1. **严格沙盒化隔离**：所有实验特性在既有官方预设中**默认保持关闭（Disabled）**，用户需在 `ExtendFX ★` 的专属实验面板中主动勾选开启；
2. **零 CPU 额外负担**：坚决避开 CPU 端复杂遍历，所有核心逻辑均运行在 GPU 像素/计算着色器上，保护 KSP 单线程物理主循环；
3. **管线双轨自适应（Forward vs. Deferred）**：
   - 通用层：100% 兼容原生 Forward 渲染；
   - 进阶层：检测到 **Blackrack 的 Deferred 模组**（`RenderingPath.DeferredShading`）激活时，全面解锁硬件级 G-Buffer 物理材质与像素速度场通道。

### Deferred 渲染管线的战略机遇
安装 Deferred 模组后，KSP 的渲染管线发生根本性蜕变。TUFX 能够直接读取完整物理 G-Buffer，彻底打破传统前向渲染的视觉天花板：
* `_CameraGBufferTexture0`：固有色 Albedo (RGB) + 遮挡度 Occlusion (A)
* `_CameraGBufferTexture1`：物理高光色 Specular (RGB) + 光滑度/粗糙度 Smoothness (A)
* `_CameraGBufferTexture2`：硬件插值精确世界空间法线 World Normal (RGB)
* `_CameraGBufferTexture3`：自发光与环境光 Emission + Ambient (HDR 浮点缓冲)
* `_CameraMotionVectorsTexture`：逐像素全场景物体与相机屏幕空间速度矢量 (Motion Vectors)

---

## 二、 现代游戏特性对比与性能损耗深度量化评估

在 KSP 的运行环境中，**CPU 极其脆弱（单线程物理瓶颈）**，而 **现代独立 GPU 通常有充足吞吐空间**。

### 性能矩阵全景图 (1440p / 4K 环境，基准参考：RTX 3060/4060 级别)

#### 1. 通用前向兼容实验功能 (Forward & Deferred 通用)
| 规划特性 | CPU 耗时 (主线程) | GPU 耗时 (1440p) | GPU 耗时 (4K) | 显存带宽影响 | 综合性能性质 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **1. AMD FSR 1.0 EASU (渲染缩放)** | **0.00 ms (零负担)** | **-2.5 ~ -6.0 ms** | **-6.0 ~ -15.0 ms** | **大幅减轻** | **【净收益】显著暴增帧率** |
| **2. 相机快门运动模糊 (纯相机重投影)** | **< 0.01 ms** | **0.4 ~ 0.8 ms** | **1.2 ~ 1.8 ms** | 极低 | **【极轻量】电影速度感** |
| **3. 多层物理光学眩光与星芒** | **< 0.01 ms** | **0.2 ~ 0.5 ms** | **0.5 ~ 0.9 ms** | 极低（低分采样）| **【极轻量】高质感无负担** |
| **4. 宽银幕变形镜头光学 (椭圆散景与拉丝)** | **< 0.01 ms** | **0.2 ~ 0.4 ms** | **0.6 ~ 0.9 ms** | 极低（低分采样）| **【极轻量】好莱坞标志性散景** |

#### 2. Deferred 渲染专属次世代特性 (需安装 Deferred 模组)
| 规划特性 | CPU 耗时 (主线程) | GPU 耗时 (1440p) | GPU 耗时 (4K) | 显存带宽影响 | 综合性能性质 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **5. Full PBR Glossy SSR (物理光泽反射)**| **< 0.01 ms** | **1.2 ~ 2.2 ms** | **2.8 ~ 4.5 ms** | 中高（粗糙度金字塔）| **【质变】彻底告别塑料感** |
| **6. True Per-Pixel Motion Blur (逐部件模糊)**| **< 0.01 ms** | **0.6 ~ 1.2 ms** | **1.5 ~ 2.5 ms** | 低（直接读速度场）| **【质变】高速部件极度真实** |
| **7. 屏幕空间间接光 (SSGI 漫反射色溢)** | **< 0.01 ms** | **2.2 ~ 4.0 ms** | **5.0 ~ 8.5 ms** | 极高（需时域累计）| **【重度】环境融入感登顶** |
| **8. Specular Occlusion (缝隙高光消光)**| **< 0.005 ms** | **0.1 ~ 0.2 ms** | **0.2 ~ 0.4 ms** | 极微 | **【极轻量】消除暗处假荧光** |
| **9. 屏幕空间次表面散射 (SSSS 皮肤/玉石)**| **< 0.01 ms** | **0.4 ~ 0.8 ms** | **0.9 ~ 1.6 ms** | 中等（双向高斯）| **【中轻度】消除小绿人塑料感** |
| **10. 延迟体积光锥 (海量探照灯/对接灯)**| **< 0.02 ms** | **0.5 ~ 1.2 ms** | **1.2 ~ 2.4 ms** | 低（无 Overdraw）| **【中度】夜间起飞沉浸感拉满** |

---

## 三、 首发实验功能技术方案

### 1. [🧪 实验] AMD FSR 1.0 EASU (Edge-Adaptive Spatial Upsampling) & 内部渲染缩放
* **技术原理**：
  与现有已集成的第二阶段 RCAS（锐化）形成完整的 FSR 1.0 闭环。采用 12-Tap 椭圆加权非线性插值核，根据局部像素张量法向动态定向插值。
* **解决痛点**：
  在 4K/2K 分辨率下搭载大型体积云（True Volumetric Clouds）、超精细地表（Parallax 2.0）或数百部件空间站时的 GPU 填充率暴跌问题。
* **关键设计**：
  提供 77% (Ultra Quality)、67% (Quality)、59% (Balanced)、50% (Performance) 渲染缩放。仅对 3D 相机视口进行子分辨率渲染与 EASU 重建，**KSP 原生 UI、字体与仪表仍保持 100% 原生点对点输出**。

### 2. [🧪 实验] Camera-Motion Blur (基于视锥重投影的相机物理快门运动模糊)
* **技术原理**：
  Unity 原生 PPv2 运动模糊因依赖延迟渲染 G-Buffer 运动矢量而无法在 KSP Forward 管线下工作。本特性采用**纯相机速度场重投影算法**：
  $$\vec{V}_{uv} = \text{Project}(\text{ViewProj}_{current}^{-1} \cdot \text{ClipPos}) - \text{Project}(\text{ViewProj}_{prev} \cdot \text{WorldPos})$$
  直接从深度图反求像素运动矢量，沿速度矢量进行 8~16 次带抖动（Interleaved Gradient Noise）的方向模糊。
* **解决痛点**：
  消除大角速度翻滚、机动过载和超音速超低空突防时画面的生硬跳动感，提供真实的 180° / 270° 快门开角电影质感。

### 3. [🧪 实验] Anamorphic Cinema Optics (宽银幕电影光学套件：椭圆散景光斑与变形拉丝)
* **技术原理**：
  1. **柱面透镜椭圆散景（Anamorphic Oval Bokeh）**：
     重塑薄透镜弥散圆采样核。采用能量守恒椭圆投影算法：
     $$\Delta X = r \cdot \frac{\cos\theta}{\sqrt{\text{Ratio}}}, \quad \Delta Y = r \cdot \sin\theta \cdot \sqrt{\text{Ratio}}$$
     保持弥散圆总光通量（光斑面积）恒定，将圆形散景连续拉伸为好莱坞变形镜头标志性的纵向细长椭圆光斑。
  2. **多层级全屏光谱横向拉丝（Anamorphic Horizontal Streak）**：
     高光提取后经由金字塔递归横向单维模糊，配合波长分色偏移生成从中心高白到边缘蓝紫/彩虹色的水平光条。
  3. **透镜同轴反射鬼影光斑（Lens Ghosts & Halo）**：
     沿画面中心对称轴反向采样强光源生成多级次级透镜反射光斑组。
* **专属滑块设计 (Dedicated Sliders)**：
  - **`Anamorphic Ratio` (宽银幕挤压比独立滑块)**：范围 0.5x ~ 3.0x（1.0 为球面正圆，1.5 为现代宽银幕，2.0 为经典 Panavision 竖向椭圆散景）；
  - **`Streak Intensity / Length` (拉丝强度与跨度独立滑块)**；
  - **`Ghost Intensity` (透镜同轴鬼影反射独立滑块)**。

### 4. [🧪 实验] Forward SSSR (前向兼容半分辨率屏幕空间镜面反射)
* **技术原理**：
  无需 G-Buffer 支持。基于深度图连续边缘重构法线，以 $1/2$ 分辨率进行屏幕空间光线步进求交（Raymarching），随后经由联合双边上采样（Joint Bilateral Upsampling）平滑滤波合成。
* **解决痛点**：
  终结飞船和地表的塑料质感。飞船在发射台起飞、降落水面或两舰轨道交会时，金属外壳与地面产生真实的实时镜面倒影。

### 5. [🧪 实验] Physical Lens Flare & Starburst (多层物理镜头眩光与光圈星芒)
* **技术原理**：
  提取场景中主光源（恒星/太阳）的 HDR 过曝坐标，通过程序化光学模拟多层透镜内二次反射光斑（Aperture Ghosts）、光圈叶片衍射星芒（Iris Diffraction Starburst）与镜头镀膜边缘紫红/翠绿衍射光环。

---

## 四、 Deferred 渲染管线专属次世代高级特性方案 (Deferred-Exclusive Suite)

当检测到环境中已安装并激活 **Deferred 模组**（`Camera.main.actualRenderingPath == RenderingPath.DeferredShading`）时，TUFX 可直接绕开 Forward 管线的限制，解锁以下现代 3A 级画面特性：

### 1. [🧪 Deferred 专属] Full PBR Glossy SSR (物理粗糙度屏幕空间镜面与光泽反射)
* **技术原理**：
  直接读取硬件 G-Buffer：
  - 法线来自 `_CameraGBufferTexture2.rgb`（消除屏幕空间深度重建法线的阶梯状锯齿）；
  - 粗糙度与高光反射率来自 `_CameraGBufferTexture1`（RGB 为 Specular $F_0$，Alpha 为 Smoothness）。
  采用 **Hi-Z 层次化深度光线步进（Hierarchical Z-Buffer Raymarching）** 与 **预滤波粗糙度金字塔（Prefiltered Roughness Mip-Chain）**：
  光线击中场景后，根据表面粗糙度等级采样对应 Mip 层的模糊颜色；金属表面与绝缘体严格按电介质菲涅尔方程衰减。
* **视觉质变**：
  - 飞船座舱玻璃、太阳翼硅板、水体呈现清晰倒映出星球表面、星空与附近机身部件的镜面反射；
  - 铝合金燃料罐、防热瓦、发射台地面呈现柔和渐进的磨砂漫反射倒影，彻底根除“要么没反射、要么全像不锈钢镜子”的粗糙质感。

### 2. [🧪 Deferred 专属] True Per-Pixel & Sub-Object Motion Blur (真实逐像素与逐部件物理运动模糊)
* **技术原理**：
  直接读取 Unity Deferred 模组输出的 `_CameraMotionVectorsTexture`（逐像素屏幕空间速度矢量缓冲）：
  $$\vec{V}_{pixel} = \text{SAMPLE\_TEXTURE2D}(\_CameraMotionVectorsTexture, \text{uv}).xy$$
  结合速度缓冲与深度权重，沿局部速度矢量进行 8~16 次带泊松抖动的加权均值模糊。
* **视觉质变**：
  彻底突破“只有摄像机旋转才有模糊”的前向妥协：
  - 旋翼机螺旋桨与直升机桨叶在高速旋转时产生真实的圆形风暴残影；
  - 固体助推器（SRB）级间分离时，高速向后翻滚脱离的火箭部件带着强烈速度残影划过屏幕；
  - 超音速贴地狂飙时，机翼边缘和地面掠过物产生如《微软模拟飞行 2024》般的极致速度冲击力。

### 3. [🧪 Deferred 专属] Screen Space Global Illumination (SSGI 屏幕空间单跳漫反射间接光与溢色)
* **技术原理**：
  - 固有色提取自 `_CameraGBufferTexture0.rgb`（无光照纯粹漫反射反照率 Albedo）；
  - 表面几何取自 GBuffer 法线与线性深度。
  在半分辨率下沿表面法线半球投射 4~8 条余弦加权随机光线，采样击中点的发光（Emission）与漫反射光子，经由时域积累滤波器（TAA-style Temporal Accumulation）降噪后，以加法混合叠加入场景间接光照层。
* **视觉质变**：
  - **漫反射颜色溢出（Color Bleeding）**：在烈日直射下，巨大的橙色外燃料箱（Jumbo-64）会把温暖的橙红色漫反射光子投射到底下白色机腹与航天飞机机翼阴影面中；
  - **地表环境光融入**：当低空掠过坎巴拉碧绿的平原草原或金色沙漠时，机腹不再是死板灰暗的底色，而是被大地自然染上鲜活的草绿色或暖黄色地表光晕。

### 4. [🧪 Deferred 专属] Specular Occlusion (GBuffer 物理镜面高光遮蔽与微几何缝隙消光)
* **技术原理**：
  在 PBR 光学中，微几何缝隙（Micro-Cavities）不仅能阻挡漫反射环境光（AO），更会几何性遮挡入射的镜面反射光线。
  利用 GBuffer2 几何法线、GBuffer1 表面粗糙度与 GTAO 的可见性锥角积分计算高光遮蔽率：
  $$\text{SpecAO} = \text{saturate}\left( (\vec{N} \cdot \vec{V} + \text{AO}) \cdot \text{Roughness} \right)$$
* **视觉质变**：
  彻底解决机械模型在暗部“假反光”的廉价塑料感。发动机导管、喷管内壁、桁架深层与铰链接缝即使是高反射金属，在阴影深处高光也会被强力消光压暗，形成深邃沉稳的重工业机械层次感。

### 5. [🧪 Deferred 专属] Screen-Space Subsurface Scattering (SSSS 屏幕空间次表面散射)
* **技术原理**：
  利用柯西/双边高斯分离核（Separable SSSS），根据材质遮罩在屏幕空间对半透光表面扩散漫反射光线，模拟光子在表皮内部数毫米到数厘米的折射与次表面吸收。
* **视觉质变**：
  - **坎巴拉人航天员生机化**：彻底告别“绿塑料公仔玩具感”，EVA 航天员的脸庞和手部在阳光侧照时，耳廓与脸颊边缘透出微微的暖黄色红晕，呈现出鲜活柔软的生物皮肤质感；
  - **冰雪星球半透明翡翠感**：Minmus（米诺斯）冰封湖泊裂隙、Vall 与 Eeloo 的极地厚冰层在太阳背光侧透出宛如玉石翡翠般的半透明微光晶莹质感。

### 6. [🧪 Deferred 专属] Deferred Volumetric Light Cones (海量局部灯光与延迟体积光锥)
* **技术原理**：
  Deferred 管线下点光源与聚光灯被作为凸多面体几何网格渲染，**增加光源无需对全场景网格执行 Overdraw**。
  在光锥网格渲染时直接注入体素/射线步进体积散射，单 Pass 完成光照累加与体积雾气光束。
* **视觉质变**：
  - 航天发射台四周数十盏巨型高杆探照灯、空间站对接口信号灯群、飞船降落架强光射灯**全开不掉帧**；
  - 强光穿过高空薄云、发射架蒸汽或火箭喷流扬尘时，投射出震撼的三维光束锥体（Spotlight Dust Shafts）。

---

## 五、 代码实现与工程规划

### 1. 着色器源码清单 (`Unity/Assets/Shaders/TUFX/`)
* `SpectralBokeh.shader`：升级支持能量守恒柱面椭圆变形散景与色散边缘；
* `AnamorphicFlare.shader`：升级全屏高光拉丝、多级金字塔与透镜同轴反射鬼影；
* `FidelityFX_EASU.shader`：AMD FSR 1.0 EASU 核心重建着色器；
* `CameraMotionBlur.shader`：视锥逆矩阵深度速度场重构与方向模糊着色器；
* `ForwardSSSR.shader`：前向前瞻型屏幕空间镜面反射与双边滤波着色器；
* `PhysicalLensFlare.shader`：程序化多透镜物理眩光与光圈衍射星芒着色器；
* `DeferredSSR.shader`：读取 GBuffer1/2 的 Hi-Z 物理光泽反射着色器；
* `DeferredSSGI.shader`：读取 GBuffer0/2 的时域间接光漫反射溢色着色器；
* `DeferredSubsurface.shader`：双边高斯屏幕空间次表面散射着色器。

### 2. C# 驱动与渲染管线清单 (`Plugin/TUFX/PostProcessing/Effects/`)
* `SpectralBokeh.cs`：增加独立滑块 `anamorphicRatio`（0.5x~3.0x）与多级目标自动测距；
* `AnamorphicFlare.cs`：增强横向拉丝跨度与透镜鬼影多层反射；
* `EASUUpscaler.cs`：管理子分辨率 RT 分配与比例枚举（50%~100%）；
* `CameraMotionBlurEffect.cs`：记录与传递 `_PrevViewProjMatrix`，控制快门开角与采样数；
* `ForwardSSSREffect.cs`：管理半分辨率步进与粗糙度截断；
* `PhysicalLensFlareEffect.cs`：主光源视锥可见性追踪与高光层级渲染；
* `DeferredSSREffect.cs`：驱动 GBuffer 物理反射与粗糙度 Mip-Chain 降采样；
* `DeferredSSGIEffect.cs`：管理半分辨率光线投射与时域累加历史缓冲区；
* `DeferredSubsurfaceEffect.cs`：管理次表面分离核横向/纵向扩散通道。

### 3. 配置与界面 (`Plugin/TUFX/GUI/ConfigurationGUI.cs`)
在 `ExtendFX ★` 面板下方新增专门的黄色实验性警示折叠区（依据 `RenderingPath.DeferredShading` 动态呈现可用特性）：
```
=====================================================
🧪 EXPERIMENTAL FEATURES / 实验功能 (Beta)
⚠️ 实验功能仅供前沿画质探索，默认保持关闭，不影响稳定预设。
=====================================================
【通用/Forward 模式实验功能】
[ ] Anamorphic Cinema Optics (宽银幕电影光学套件)
    ├── Anamorphic Ratio (椭圆散景比): [ 2.0x ] (经典 Panavision 竖向椭圆)
    ├── Horizontal Streak (全屏蓝光拉丝): [ 1.5 ]
    └── Lens Ghosting (同轴透镜反射鬼影): [ 0.8 ]
[ ] AMD FSR 1.0 EASU (Render Scale / 内部超分放大)
[ ] Camera-Motion Blur (相机物理快门运动模糊)
[ ] Forward SSSR (前向屏幕空间镜面高光反射)
[ ] Physical Lens Flare (物理多层镜头眩光与光圈星芒)

【Deferred 延迟管线专属次世代功能】(需检测到 Deferred 模组)
[ ] Full PBR Glossy SSR (基于 GBuffer 物理粗糙度的光泽反射)
[ ] True Per-Pixel Motion Blur (基于 GBuffer 速度矢量的逐部件运动模糊)
[ ] Screen-Space Global Illumination (SSGI 屏幕空间间接光漫反射色溢)
[ ] Specular Occlusion (GBuffer 物理镜面高光消光)
[ ] Screen-Space SSSS (次表面散射 / 航天员皮肤与冰川微光)
```

### 4. 预设配置部署 (`GameData/TUFX/Profiles/`)
* 保留现有所有稳定预设原貌；
* 在 `TUFX-CinematicAdvanced.cfg` 中新增独立预设 `Cinematic-NextGen-Experimental` 与 `Cinematic-Deferred-Extreme`，方便用户直接体验完整实验功能。

---

## 六、 验证与测试方案

1. **编译测试**：
   - Unity 2019.4.18f1 重新构建打包 `tufx-advanced.ssf`（验证 0 错误）；
   - MSBuild 重新编译 `Plugin/TUFX.sln`（Release 模式，验证 0 错误）。
2. **游戏内功能性验证**：
   - **EASU**：在 4K 分辨率下拉动渲染缩放滑块至 67%，验证帧率显著上升且 UI 文字 100% 保持原生清晰点对点；
   - **Deferred PBR SSR**：在装有 Deferred 模组的发射台观察火箭金属漆面与地面水泥，验证粗糙度分级模糊倒影；
   - **Per-Pixel Motion Blur**：高速旋转飞船或分离助推器，验证飞过的部件独立带模糊拖影，机体不模糊；
   - **SSGI 色溢**：在阳光直射下将橙色大油罐靠近白色舱体，观察背光处机腹是否自然映上温暖橙光；
   - **稳定性隔离**：切回经典 `Cinematic-NextGen-CMAA2` 预设，确认所有实验功能关闭时性能与画质无任何劣化。
