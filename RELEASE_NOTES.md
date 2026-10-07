# PointCloudViz v2.0.0

PointCloudViz 2.0 是一次全面升级：核心算法独立为跨平台类库并配有单元测试，桌面程序以 MVVM 重写，新增命令行工具 `pcv`。

## 下载

| 文件 | 说明 |
| --- | --- |
| `PointCloudViz-win-x64.zip` | 桌面程序，自包含单文件，解压即用（Windows 10/11，DirectX 11） |
| `pcv-win-x64.zip` / `pcv-linux-x64.zip` / `pcv-osx-arm64.zip` | 命令行工具 |

## 亮点

- 大坐标（UTM 等）毫米级精度；Z 轴朝上的相机；相机不再被意外重置
- LAS 1.0–1.4 全部点格式读取与写出，二进制 PLY 读写
- 真彩色 / 高程 / 强度 / 分类着色，多种色带与图例，分类显示开关
- 统计离群点剔除、KD 树加速的半径离群点剔除、体素下采样，全部可撤销、可取消
- 坐标 / 距离（斜距、平距、高差、坡度）/ 折线 / 面积（空间与水平投影）量测，CSV 导出
- 项目文件保存相机与量测；拖放打开、最近文件、截图、快捷键

完整变更见 [CHANGELOG.md](https://github.com/SuLiverse/PointCloudViz_Final/blob/main/CHANGELOG.md)。
