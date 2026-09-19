# TUFX: Beyond - Textures Unlimited Effects: Beyond

[![Kerbal Space Program](https://img.shields.io/badge/KSP-1.12.x-brightgreen.svg)](https://www.kerbalspaceprogram.com/)
[![Unity](https://img.shields.io/badge/Unity-2019.4.18f1-blue.svg)](https://unity.com/)
[![Fork](https://img.shields.io/badge/GitHub-DiaoDaiaChan%2FTUFX-181717.svg?logo=github)](https://github.com/DiaoDaiaChan/TUFX)
[![License](https://img.shields.io/badge/License-GPL%20v3%20%2F%20MIT-orange.svg)](https://github.com/DiaoDaiaChan/TUFX)

**TUFX: Beyond** 是针对 **Kerbal Space Program 1 (KSP)** 的次世代影视级与延迟渲染光影重构后处理套件。基于 Unity Post-Processing Stack v2 深度重构，全方位突破原版前向管线束缚，深度融合 **Deferred (延迟渲染管线)** 硬件级 G-Buffer 与现代 3A 开放图形算法，并提供纯净、零 GC 的全管线性能探针与原生全功能中英双语国际化支持。

---

## ✨ 核心特性矩阵 (Feature Highlights)

### 📊 全管线实时性能监控器 (Zero-GC Pipeline Profiler)
* **CPU 高精度硬件探针**：基于 `Stopwatch.GetTimestamp()`（RDTSC 硬件指令），单次探针开销仅 ~10ns，**零堆内存分配（Zero Allocation）**，对 60/144 FPS 物理主循环无任何额外 GC 负担；
* **确定性硬件管线排序**：按 Unity 后处理生命周期严格排序：`[1. Opaque] → [2. BeforeStack] → [3. Builtins] → [4. AfterStack] → [5. FinalPass]`；
* **全景遥测仪表盘**：实时帧率 (FPS)、单帧延迟 (ms)、TUFX 渲染耗时 (ms)、当前激活通道数，配套高科技动态占比条（如 `[████░░░░░░] 38.2%`）与历史掉帧峰值记录。

### 🌐 原生多语言国际化系统 (Native i18n Localization)
* **无缝接入 KSP 本地化引擎**：全面适配 `KSP.Localization.Localizer`，提供标准模块化字典（`zh-cn.cfg` 与 `en-us.cfg`）；
* **100% 界面全覆盖**：所有设置窗口、导航按钮、次世代折叠组、参数标签、技术提示（Tooltips）与色彩控件均已支持多语言即时切换，具备绝对安全的回退机制，不暴露底层 `#LOC_` 标签。

### 🎥 宽银幕电影光学套件 (Anamorphic Cinema Optics)
* **能量守恒柱面椭圆变形散景 (Anamorphic Oval Bokeh)**：重塑薄透镜弥散圆采样核，采用椭圆能量守恒投影 $\vec{u} = (\cos\theta / \sqrt{R}, \sin\theta \sqrt{R})$，提供 `Anamorphic Ratio` (0.5x~3.0x) 独立调节，真实呈现 Panavision 经典纵向细长椭圆光斑与边缘色散；
* **32-Tap 高阶光谱横向拉丝与透镜鬼影 (Spectral Streaks & Ghosts)**：全屏非线性幂次横向拉丝与多级画面反向中心对称同轴透镜反射环；
* **CinemaScope 宽银幕电影遮幅 (Cinematic Letterboxing)**：一键开启好莱坞 2.39:1 / 21:9 电影黑边遮幅，并支持遮幅占比自定义。

### 🏎️ 真实快门相机关联与逐物体运动模糊 (Camera & Sub-Object Motion Blur)
* **纯相机速度场解析重构**：通过两帧逆视锥投影矩阵 $(\text{CurrInvViewProj} \to \text{PrevViewProj})$ 从深度直接反解速度场，赋予机动与超音速突防 180° / 270° 旋转快门电影质感；
* **[Deferred 专属] 真实逐物体运动矢量融合**：直接接入 `_CameraMotionVectorsTexture`，支持高速旋转螺旋桨、旋翼桨叶、级间分离火箭部件的局部物理残影。

### ⚡ AMD FidelityFX FSR 1.0 EASU + RCAS 超分辨率空间升采样
* **12-Tap 边缘自适应空间升采样 (EASU)**：通过局部梯度各向异性张量核沿边缘走向执行 Lanczos2 逼近插值，内置 Anti-Ringing 极值钳制根除白边与伪影；
* **级联物理锐化 (RCAS)**：原生提供 Ultra Quality (77%)、Quality (67%)、Balanced (59%)、Performance (50%) 与 Native 5 档预设，极大释放高分辨率渲染压力。

### 🛡️ 混合屏幕空间反射系统 (SSR: Forward SSSR / Deferred PBR SSR)
* **自研自适应双管线架构**：
  - **Deferred 模式**：基于 GBuffer0/1/2 几何法线与粗糙度执行多 Mip 金字塔预滤波反射，融合反射探针与 PBS 间接高光；
  - **Forward 模式 (SSSR)**：基于交叉深度重构法线与 Schlick 菲涅尔方程，即使在原生 Forward 管线下也能在水面、跑道与金属机身上获得平滑实时的屏幕空间镜面倒影。

### ☀️ 延迟渲染微几何高光遮蔽与硬件法线 (Specular Occlusion & XeGTAO)
* **GBuffer2 硬件几何法线直采**：消除深度反求法线在边缘处的阶梯锯齿与平面阴影伪影；
* **Lagarde (Frostbite) PBR Specular Occlusion**：
  $$\text{specAO} = \text{saturate}\left( (\vec{N} \cdot \vec{V} + \text{ao})^{\exp_2(-16 \cdot \text{roughness} - 1)} - 1 + \text{ao} \right)$$
  在深邃缝隙压暗假高光泄漏，并在光滑金属/太阳能帆板上智能保留高光，杜绝漫反射 AO 涂黑镜面的失真。

### 🌈 屏幕空间单跳漫反射全局光照 (SSGI - Color Bleeding)
* **黄金分割螺旋光线步进**：半分辨率法线半球黄金分割螺旋（Golden Ratio Spiral）余弦加权光线投射；
* **间接光漫反射反弹**：采集场景命中点色彩，结合 GBuffer0 漫反射固有色（Albedo）与几何距离衰减，经双边滤波平滑融合，产生真实的橙色燃料罐反光与低空掠过地表时的环境漫反射光晕。

### 🧊 智能自适应次表面散射 (Smart-Adaptive SSSS)
* **Jimenez 可分离次表面散射**：基于 6-Tap 可分离柯西/高斯核，水平与垂直两 Pass 扩散漫反射光线；
* **材质色度智能自适应 (Auto Adapt)**：自动解析表面反射固有色饱和度与色度 —— **金属航天器蒙皮保持纯净中性冷光，杜绝暖色偏色**；坎巴拉宇航员 EVA 皮肤自动获得通透柔软的浅绿色皮下微光，冰雪天体呈现玉石通透感。

### 💎 零重影抗锯齿与现代色彩科学 (CMAA 2 & Modern Tonemapping)
* **Intel CMAA 2**：纯单帧保守形态学抗锯齿，高速变轨翻滚 **100% 零拖尾、零残影**；
* **电影级色彩科学**：内置 **AgX (Blender 4.0 标配)**、ACES Filmic、Tony McMapface 与 Gran Turismo 色调映射。

---

## 📖 详细技术规范与文档 (Documentation)

* **[walkthrough.md](walkthrough.md)**：最新功能实现报告，涵盖性能监控器、多语言架构、各着色器数学原理。
* **[BRANCH_FEATURES.md](BRANCH_FEATURES.md)**：全特性开发者规范、管线确定性排序表格、着色器属性绑定规则与故障排查指南。
* **[EXPERIMENTAL_FEATURES_PLAN.md](EXPERIMENTAL_FEATURES_PLAN.md)**：图形学进阶演进路线图与性能损耗全景评估。

---

## 🛠️ 编译与构建 (Building from Source)

### 编译着色器包 (`tufx-advanced.ssf`)
```powershell
& "C:\Program Files\Unity\Hub\Editor\2019.4.18f1\Editor\Unity.exe" `
  -batchmode -quit `
  -projectPath "c:\Users\43701\Documents\github\TUFX\Unity" `
  -executeMethod BuildAdvancedBundle.BuildAll `
  -logFile "c:\Users\43701\Documents\github\TUFX\unity_build.log"
```

### 编译 C# 插件 (`TUFX.dll`)
```powershell
dotnet msbuild "Plugin\TUFX.sln" /p:Configuration=Release
```

---

## 📜 开源致谢与许可 (Credits & Licenses)
* **TUFX: Beyond Author**: [DiaoDaiaChan](https://github.com/DiaoDaiaChan/TUFX)
* **Original TUFX**: [shadowmage45](https://github.com/shadowmage45/TUFX) (GPL-3.0 License)
* **Intel XeGTAO & CMAA 2**: [Intel Corporation & Jorge Jimenez, Filip Strugar](https://github.com/GameTechDev) (MIT / Apache-2.0)
* **AMD FidelityFX (EASU / RCAS / CAS)**: [Advanced Micro Devices](https://github.com/GPUOpen-Effects/FidelityFX-FSR) (MIT License)
* **Lagarde PBR Specular Occlusion**: Sebastien Lagarde (Frostbite Engine, Electronic Arts)
* **Separable Subsurface Scattering**: Jorge Jimenez et al.
* **AgX Color Science**: Blender Foundation & Troy Sobotka
