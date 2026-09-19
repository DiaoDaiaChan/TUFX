# TUFX 次世代视觉与延迟渲染增强特性实现总结报告

本轮开发已全面实现并在 KSP 运行环境中成功编译部署《EXPERIMENTAL_FEATURES_PLAN.md》规划的所有次世代视觉特效模块。每个模块均包含独立 C# 驱动参数、高性能 GPU 着色器通道以及在 In-Game UI 中的参数滑块与状态反馈。

---

## 模块实施与技术架构总览

### 1. 宽银幕电影光学套件 (Anamorphic Cinema Optics)
- **文件**：[`SpectralBokeh.shader`](file:///c:/Users/43701/Documents/github/TUFX/Unity/Assets/Shaders/TUFX/SpectralBokeh.shader), [`SpectralBokeh.cs`](file:///c:/Users/43701/Documents/github/TUFX/Plugin/TUFX/PostProcessing/Effects/SpectralBokeh.cs), [`AnamorphicFlare.shader`](file:///c:/Users/43701/Documents/github/TUFX/Unity/Assets/Shaders/TUFX/AnamorphicFlare.shader), [`AnamorphicFlare.cs`](file:///c:/Users/43701/Documents/github/TUFX/Plugin/TUFX/PostProcessing/Effects/AnamorphicFlare.cs)
- **技术原理**：
  - **能量守恒柱面椭圆散景**：通过 $\vec{u} = (\cos\theta / \sqrt{R}, \sin\theta \sqrt{R})$ 保证弥散圆面积守恒，提供 `Anamorphic Ratio` (0.5x~3.0x) 纵向细长好莱坞变形光斑。
  - **32-Tap 光谱横向拉丝与透镜鬼影**：横向非线性幂次采样、红绿蓝波长色散分离与画面反向中心对称多级同轴透镜反射环。

---

### 2. 快门相机关联与逐物体运动模糊 (Camera & Sub-Object Motion Blur)
- **文件**：[`CameraMotionBlur.shader`](file:///c:/Users/43701/Documents/github/TUFX/Unity/Assets/Shaders/TUFX/CameraMotionBlur.shader), [`CameraMotionBlurEffect.cs`](file:///c:/Users/43701/Documents/github/TUFX/Plugin/TUFX/PostProcessing/Effects/CameraMotionBlurEffect.cs)
- **技术原理**：
  - **前向兼容模式**：基于两帧逆视锥投影矩阵 $(\text{CurrInvViewProj} \to \text{PrevViewProj})$ 从深度直接反求屏幕空间像素速度场，彻底摆脱前向管线无速度缓冲的限制。
  - **延迟模式逐物体运动矢量融合**：自动检测并读取 Unity Deferred 的 `_CameraMotionVectorsTexture`，支持旋翼桨叶、级间分离火箭等部件自身运动的高精度物理残影。
  - **IGN 抖动与动态快门**：支持电影级 180° / 270° 旋转快门开角，在亚像素静止时自动零开销早退。

---

### 3. AMD FidelityFX 超分辨率空间升采样 (FSR 1.0 EASU + RCAS)
- **文件**：[`FidelityFX_EASU.shader`](file:///c:/Users/43701/Documents/github/TUFX/Unity/Assets/Shaders/TUFX/FidelityFX_EASU.shader), [`EASUUpscaler.cs`](file:///c:/Users/43701/Documents/github/TUFX/Plugin/TUFX/PostProcessing/Effects/EASUUpscaler.cs)
- **技术原理**：
  - **12-Tap 方向各向异性张量核**：通过梯度加权与特征向量分析边缘倾角，沿边缘轴执行 Lanczos2 逼近插值，内置 Anti-Ringing 局部极值钳制防止白边与过冲。
  - **级联 RCAS 物理锐化**：自动在空间升采样后衔接高动态对比度自适应锐化，原生支持 5 级质量预设（Ultra Quality 77%、Quality 67%、Balanced 59%、Performance 50% 与 Native 100%）。

---

### 4. 延迟渲染镜面高光遮蔽与硬件法线集成 (Specular Occlusion & XeGTAO Normal)
- **文件**：[`GroundTruthAO.shader`](file:///c:/Users/43701/Documents/github/TUFX/Unity/Assets/Shaders/TUFX/GroundTruthAO.shader), [`GroundTruthAO.cs`](file:///c:/Users/43701/Documents/github/TUFX/Plugin/TUFX/PostProcessing/Effects/GroundTruthAO.cs)
- **技术原理**：
  - **GBuffer2 硬件法线注入**：在 Deferred 模式下直接读取 `_CameraGBufferTexture2.rgb` 表面法线，彻底消除深度反求法线在边缘处易产生的阶梯状锯齿与条纹伪影。
  - **Lagarde (Frostbite) PBR Specular Occlusion**：
    $$\text{specAO} = \text{saturate}\left( (\vec{N} \cdot \vec{V} + \text{ao})^{\exp_2(-16 \cdot \text{roughness} - 1)} - 1 + \text{ao} \right)$$
    从 `_CameraGBufferTexture1` 读取光滑度与高光反射率，使粗糙表面享受深邃微几何阴影，同时保护光滑金属高光不会被漫反射阴影不合常理地涂黑。

---

### 5. 混合屏幕空间反射系统 (Screen Space Reflections: Forward SSSR / Deferred PBR SSR)
- **文件**：[`ScreenSpaceReflections.shader`](file:///c:/Users/43701/Documents/github/TUFX/Unity/Assets/Shaders/TUFX/ScreenSpaceReflections.shader), [`ScreenSpaceReflections.cs`](file:///c:/Users/43701/Documents/github/TUFX/Plugin/TUFX/PostProcessing/Effects/ScreenSpaceReflections.cs)
- **技术原理**：
  - 自研双管线自适应着色器 `Hidden/TUFX/ScreenSpaceReflections`，破除了原版仅允许 Deferred 运行的硬编码限制。
  - **Deferred 模式**：基于 GBuffer0/1/2 真实法线与粗糙度执行多 Mip 金字塔下采样反射，融合反射探针与 PBS 间接高光。
  - **Forward 模式 (SSSR)**：基于边缘自适应交叉深度重构法线与 Schlick 菲涅尔方程，即使在原生 Forward 管线下也能在水面、跑道与金属外壳上获得平滑实时的屏幕空间镜面倒影。

---

### 6. 屏幕空间漫反射全局光照与溢色 (SSGI)
- **文件**：[`SSGI.shader`](file:///c:/Users/43701/Documents/github/TUFX/Unity/Assets/Shaders/TUFX/SSGI.shader), [`SSGIEffect.cs`](file:///c:/Users/43701/Documents/github/TUFX/Plugin/TUFX/PostProcessing/Effects/SSGIEffect.cs)
- **技术原理**：
  - 在半分辨率下基于黄金分割螺旋（Golden Ratio Spiral）在法线半球投射 4~8 条余弦加权射线。
  - 采集光线命中点场景色彩，结合 GBuffer0 漫反射固有色（Albedo）与几何距离衰减，经双边滤波器升采样平滑融合，产生真实的橙色燃料罐反光与地表绿色/黄色环境漫反射光晕。

---

### 7. 距离自适应屏幕空间次表面散射 (Distance-Adaptive Separable SSSS)
- **文件**：[`SubsurfaceScattering.shader`](file:///c:/Users/43701/Documents/github/TUFX/Unity/Assets/Shaders/TUFX/SubsurfaceScattering.shader), [`SubsurfaceScatteringEffect.cs`](file:///c:/Users/43701/Documents/github/TUFX/Plugin/TUFX/PostProcessing/Effects/SubsurfaceScatteringEffect.cs)
- **问题根因定位 (效果消失原因)**：
  1. **极小距离硬截断**：原着色器在深度超过 `_MaxDistance` (原默认仅 25.0 米) 时直接返回原图。在 KSP 航天飞行、火箭升空或中远景观察载具时（视距通常 50m~250m），SSSS 处于 100% 彻底关闭状态。
  2. **亚像素采样核坍缩**：原核半径按纯物理 $1/d$ 比例收缩，在 20m 视距外采样半径仅 0.2~0.3 像素，扩散完全不可察。
  3. **固定深度阈值在远景失真**：原双边滤波阈值固定为 8 厘米（0.08m）。在 50m~100m 观察圆柱形火箭储箱时，透视曲率导致的深度差轻易突破 8cm，双边权重直接归零，导致扩散核在载具表面被完全过滤。
  4. **低饱和度表面削减惩罚**：原着色器对低饱和度表面衰减 65% 强度，导致白色/金属质感航天器蒙皮上几乎无效果。
- **技术升级与修复**：
  - **动态视距自适应核半径**：引入 `clamp((_ScatterRadius * 6.5) / max(1.0, centerDepth * 0.12), 2.0, 24.0)`，保证近距离 EVA 呈现细腻有机次表面柔光的同时，在中远视距下仍维持至少 2.0 像素的有效柔化足迹。
  - **透视深度自适应双边阈值**：`depthTol = max(0.03, _DepthThreshold * max(1.0, centerDepth * 0.05))`，随视距动态拉伸深度容差，允许扩散核在火箭弧形储箱与机翼曲面上完整扩散，同时对背景天空与远景保持锐利边缘隔离。
  - **色差防逆转明暗扩散**：将扩散量分离为正向受光渗透 `max(0.0, diff) * tintColor` 与负向高光收敛 `min(0.0, diff)`，既保证阴影明暗交界线呈现生物/材质透光微光，又杜绝亮部产生色相倒转与偏色伪影。
  - **视距上限扩展**：默认最大生效距离由 25m 大幅提升至 350m（UI 支持滑动至 1000m），末端 25% 距离平滑淡出，彻底激活全飞行阶段的次表面透光与高光边缘柔化质感。

---

## 编译与部署验证

| 验证项目 | 执行命令 / 目标 | 状态 |
| :--- | :--- | :--- |
| **Unity AssetBundle 构建** | `BuildAdvancedBundle.BuildAll` 批量打包全部高级 Shader |  0 错误，生成 `tufx-advanced.ssf` |
| **C# 插件编译** | `dotnet msbuild Plugin/TUFX.sln /p:Configuration=Release` |  0 错误，生成 `TUFX.dll` |
| **KSP GameData 部署** | 目标路径 `Kerbal Space Program_newmod/GameData/TUFX/` |  着色器包与 DLL 均已成功覆盖部署 |
