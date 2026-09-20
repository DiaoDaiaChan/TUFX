# chaiNNer 游戏贴图超分与 ONNX 导出指南 (针对 TUFX DirectML 管线)

本指南介绍如何使用 **chaiNNer** 可视化节点工作流，将社区顶级超分模型（如 OpenModelDB 上的 SPAN、Nomos8k、PBRify、Real-ESRGAN 等）转换为兼容 TUFX DirectML 引擎的 `.onnx` 动态尺寸模型。

---

## 1. 为什么需要针对 KSP 的专用导出规范？

KSP 材质贴图具有以下两大特殊属性：
1. **动态分辨率（Dynamic Dimensions）**：不同部件贴图尺寸多样（512x512, 1024x1024, 2048x1024）。ONNX 必须导出为动态输入形状（`[1, 3, -1, -1]`）。
2. **Alpha 通道物理属性隔离**：Alpha 通道在 KSP 中往往承载金属高光度（Specular/Gloss），TUFX DirectML 引擎已内置了通道自动分流机制（RGB 进神经网络，Alpha 双三次保边放大后重组）。

---

## 2. 在 chaiNNer 中导出 ONNX 的标准步骤

### 步骤 1：准备模型与依赖
1. 打开 **chaiNNer**；
2. 点击右上角 **Dependency Manager**（依赖管理器），确保安装了 **PyTorch** 与 **ONNX** 模块；
3. 从 [OpenModelDB](https://openmodeldb.info/) 下载心仪的硬表面/游戏超分模型（例如 `.pth` 格式的 `4x-PBRify` 或 `2x-SPAN`）。

### 步骤 2：搭建节点网络
1. **Load Model (加载模型)**：
   * 选择你下载的 `.pth` 权重文件。
2. **Convert Model to ONNX (模型转 ONNX)**：
   * 连接 `Load Model` 的 Model 输出到 `Convert Model to ONNX` 节点；
   * **输入形状配置 (Input Shape)**：设为 `[1, 3, None, None]` 或启用 `Dynamic Axes`；
   * **Opset Version**：建议设为 `17` 或 `18`（DirectML 1.14+ 完美支持）；
   * **数据类型**：Float32 (FP32)；
3. **Save Model (保存模型)**：
   * 输出格式选择 `.onnx`；
   * 将保存的文件放置在你的游戏目录：
     `GameData/TUFX/Models/你的模型名.onnx`

---

## 3. 直接使用社区已有的 ONNX 模型

如果你不想自己转换，可以直接从官方和社区经过验证的 ONNX 仓库下载现成模型：
- **TUFX 预置模型目录**：`GameData/TUFX/Models/`
  * `2x-NomosUni-Compact.onnx` (通用紧凑型，单张 20~70ms)
  * `2x-SPAN-HardSurface.onnx` (硬表面机械专用，超快防抹平)
  * `4x-SPAN-PurePhoto.onnx` (4倍微观质感强化)
- 放置在 `GameData/TUFX/Models/` 下的任何 `.onnx` 模型均会被 TUFX 自动发现并在 UI 中提供切换按钮！
