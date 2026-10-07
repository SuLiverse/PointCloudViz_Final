# 示例数据

| 文件 | 说明 |
| --- | --- |
| `street_scene.las` | 合成街景（LAS 1.4 / 点格式 7），约 9 万点，含 RGB、强度与 ASPRS 分类，坐标为 UTM 量级（用于验证大坐标精度处理）。由 `pcv generate samples/street_scene.las --spacing 0.2` 生成。 |
| `sample_final.ply` | 旧版附带的 ASCII PLY 示例（4000 个随机彩色点）。 |
| `sample_final.xyz` | 旧版附带的 XYZ 示例（8000 个点，含强度列）。 |

更密的街景可以在软件中通过“文件 → 生成合成街景”或命令行 `pcv generate street.las` 生成。
