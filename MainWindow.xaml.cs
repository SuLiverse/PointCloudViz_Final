using Microsoft.Win32;
using PointCloudViz_Final.Filters;
using PointCloudViz_Final.IO;
using PointCloudViz_Final.Models;
using PointCloudViz_Final.Rendering;
using PointCloudViz_Final.Patterns;
using PointCloudViz_Final.Services;
using PointCloudViz_Final.Tools;
using PointCloudViz_Final.Utils;
using PointCloudViz_Final.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.ComponentModel;
using HelixToolkit.Wpf.SharpDX;
using HelixToolkit.Wpf.SharpDX.Core;
using SharpDX;
using Color4 = SharpDX.Color4;
using SharpDXVector3 = SharpDX.Vector3;
using WpfColor = System.Windows.Media.Color;
using WpfVector3 = System.Numerics.Vector3;
using WpfPoint = System.Windows.Point;
using WpfVector = System.Windows.Vector;
using PointCloudBBox = PointCloudViz_Final.Models.BoundingBox;
using PointCloudPlyReader = PointCloudViz_Final.IO.PlyReader;

// 解决 Camera 二义性（自定义 vs Media3D）
using Camera = PointCloudViz_Final.Models.Camera;

namespace PointCloudViz_Final
{
    public partial class MainWindow : Window
    {
        private readonly List<IPointReader> _readers = new() 
        { 
            new XyzReader(), 
            new PointCloudPlyReader(),
            new IO.StreamingLasReader(), // 优先使用流式读取器
            new BasicLasReader() // 回退选项
        };
        private IColorMap _colorMap = new HeightColorMap();
        private MainViewModel _vm = new MainViewModel();

        private PointCloud? _original;
        private PointCloud? _cloud;
        private int _pointSize = 3;
        private WpfColor _background = Colors.Black;

        private MeasurementTool _measurementTool = new MeasurementTool();
        private WpfVector3 _renderOffset = WpfVector3.Zero;
        private bool _isRotating = false;
        private bool _isPanning = false;
        private WpfPoint _lastMousePos;
        private double _cameraYaw = -35;
        private double _cameraPitch = -25;
        private double _cameraDistance = 100;
        private System.Numerics.Vector3 _cameraTarget = System.Numerics.Vector3.Zero;
        private const double RotateSpeed = 0.35;
        private const double PanSpeed = 0.003;
        private const double ZoomFactor = 1.08;
        private const double KeyboardMoveSpeed = 0.05;
        private bool _cameraInitialized = false;
        private Patterns.CommandManager _commandManager = new Patterns.CommandManager();
        private bool _lodEnabled = true; // LOD默认启用（Helix会自动处理LOD）
        private DispatcherTimer _notificationTimer = new DispatcherTimer();
        private const int LargePointPromptThreshold = 1_000_000;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = _vm;
            
            // 初始化 Helix Toolkit 的 EffectsManager（必须在 InitializeComponent 之后）
            try
            {
                if (Viewport3D != null)
                {
                    var effectsManager = new HelixToolkit.Wpf.SharpDX.DefaultEffectsManager();
                    Viewport3D.EffectsManager = effectsManager;
                    Logger.Info("已清除所有测量");
                    
                    // 启用MSAA抗锯齿
                    Viewport3D.MSAA = HelixToolkit.Wpf.SharpDX.MSAALevel.Two;
                    
                    // 确保启用渲染
                    Viewport3D.EnableRenderFrustum = false; // 禁用视锥体裁剪调试
                }
            }
            catch (Exception ex)
            {
                Logger.Error("EffectsManager 初始化失败", ex);
                MessageBox.Show($"渲染引擎初始化失败：{ex.Message}\n\n可能原因：\n1. 显卡驱动过旧\n2. DirectX版本不支持\n3. 系统不支持SharpDX", "警告");
            }
            
           
            Loaded += (_, __) => 
            {
                Logger.Info("MainWindow ???");
                if (_cloud != null)
                {
                    UpdatePointCloudGPU(_cloud, resetCamera: true);
                    PromptVoxelDownsampleIfLarge();
                }
                ShowNotification(StatusText.Text);
            };
            // 注意：Helix Toolkit 的 Viewport3DX 已经内置了鼠标和键盘交互

            // 测量工具事件
            _measurementTool.OnMeasurementCreated += (m) =>
            {
                Dispatcher.Invoke(() =>
                {
                    StatusText.Text = m.Label;
                    Logger.Info($"测量: {m.Label}");
                    ShowNotification(m.Label);
                    UpdateMeasurementVisuals();
                });
            };
            _measurementTool.OnMeasurementMessage += (msg) =>
            {
                Dispatcher.Invoke(() =>
                {
                    StatusText.Text = msg;
                    Logger.Info(msg);
                    ShowNotification(msg);
                });
            };

            Logger.Info("已清除所有测量");

            // 命令管理器事件
            _commandManager.OnCommandExecuted += (cmd) =>
            {
                Dispatcher.Invoke(() =>
                {
                    UpdateUndoRedoMenu();
                    Logger.Info($"命令执行: {cmd.Description}");
                });
            };
            _commandManager.OnCommandUndone += (cmd) =>
            {
                Dispatcher.Invoke(() =>
                {
                    UpdateUndoRedoMenu();
                    Logger.Info($"命令撤销: {cmd.Description}");
                });
            };
            _commandManager.OnCommandRedone += (cmd) =>
            {
                Dispatcher.Invoke(() =>
                {
                    UpdateUndoRedoMenu();
                    Logger.Info($"命令重做: {cmd.Description}");
                });
            };

            PreviewKeyDown += MainWindow_PreviewKeyDown;
            
            // 确保滚轮事件能被捕获
            this.AddHandler(MouseWheelEvent, new MouseWheelEventHandler(Window_MouseWheel), true);
            
            // 监控相机属性变化，同步到我们的变量
            Loaded += (s, e) =>
            {
                if (HelixCamera != null)
                {
                    System.Windows.Media.CompositionTarget.Rendering += SyncCameraState;
                }
                if (StatusText != null)
                {
                    DependencyPropertyDescriptor.FromProperty(
                        System.Windows.Controls.TextBlock.TextProperty,
                        typeof(System.Windows.Controls.TextBlock))
                        .AddValueChanged(StatusText, (_, __) =>
                        {
                            ShowNotification(StatusText.Text);
                        });
                }
            };

            _notificationTimer.Interval = TimeSpan.FromSeconds(2.5);
            _notificationTimer.Tick += (_, __) =>
            {
                if (NotificationPanel != null)
                    NotificationPanel.Visibility = Visibility.Collapsed;
                _notificationTimer.Stop();
            };
        }

        private void UpdateUndoRedoMenu()
        {
            // 通过名称查找菜单项
            var undoItem = FindName("UndoMenuItem") as System.Windows.Controls.MenuItem;
            var redoItem = FindName("RedoMenuItem") as System.Windows.Controls.MenuItem;
            
            if (undoItem != null)
                undoItem.IsEnabled = _commandManager.CanUndo;
            if (redoItem != null)
                redoItem.IsEnabled = _commandManager.CanRedo;
        }

        private void ShowNotification(string message)
        {
            if (string.IsNullOrWhiteSpace(message) || NotificationPanel == null || NotificationText == null)
                return;
            NotificationText.Text = message;
            NotificationPanel.Visibility = Visibility.Visible;
            _notificationTimer.Stop();
            _notificationTimer.Start();
        }

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            _commandManager.Undo();
        }

        private void Redo_Click(object sender, RoutedEventArgs e)
        {
            _commandManager.Redo();
        }

        /// <summary>使用 Helix Toolkit 更新点云（GPU硬件加速）</summary>
        /// <remarks>
        /// 关键优化：点云归一化（去中心化）
        /// 对于测绘工程的 UTM/高斯-克吕格坐标（通常是几百万的量级），
        /// 必须将点云移动到原点附近，否则会导致：
        /// 1. 相机找不到点（点云在几百公里外）
        /// 2. 精度丢失（float 精度问题导致闪烁或乱码）
        /// </remarks>
        /// <param name="cloud">点云数据</param>
        /// <param name="addTestPoint">是否添加测试点（用于调试黑屏问题）</param>
        private void UpdatePointCloudGPU(PointCloud cloud, bool addTestPoint = false, bool resetCamera = false)
        {
            if (cloud == null || PointGeometry == null || PointModel == null) return;

            try
            {
                var points = cloud.Points;
                if (points == null || points.Count == 0)
                {
                    StatusText.Text = "已清除所有测量";
                    return;
                }

                var bbox = cloud.BBox;
                
                // --- 第一步：计算中心点（用于去中心化）---
                // 使用包围盒中心作为偏移量
                double offsetX = (bbox.MinX + bbox.MaxX) / 2.0;
                double offsetY = (bbox.MinY + bbox.MaxY) / 2.0;
                double offsetZ = (bbox.MinZ + bbox.MaxZ) / 2.0;
                _renderOffset = new WpfVector3((float)offsetX, (float)offsetY, (float)offsetZ);

                // 应用LOD：如果启用且点数过多，进行下采样
                var pointsToRender = points;
                if (_lodEnabled && points.Count > 1_000_000)
                {
                    // 对于百万级点云，采样到100万点
                    int step = Math.Max(1, points.Count / 1_000_000);
                    pointsToRender = points.Where((p, i) => i % step == 0).ToList();
                }

                // 准备数据容器
                var positions = new Vector3Collection(pointsToRender.Count);
                var colors = new Color4Collection(pointsToRender.Count);

                // --- 第二步：转换数据并去中心化 ---
                foreach (var p in pointsToRender)
                {
                    // 关键操作：减去偏移量，把坐标移到原点附近！
                    // 注意：转成 float 是安全的，因为减去偏移后数值很小
                    float x = (float)(p.X - offsetX);
                    float y = (float)(p.Y - offsetY);
                    float z = (float)(p.Z - offsetZ);

                    positions.Add(new SharpDXVector3(x, y, z));

                    // 颜色转换（使用颜色映射）
                    // 确保 Alpha 通道是 1.0f（如果是 0 就完全透明了！）
                    var wpfColor = _colorMap.Map(p, bbox);
                    colors.Add(new Color4(
                        wpfColor.R / 255f,
                        wpfColor.G / 255f,
                        wpfColor.B / 255f,
                        1.0f)); // Alpha = 1.0f，完全不透明
                }

                // 调试：如果启用，添加一个测试点（红色，在原点）
                if (addTestPoint)
                {
                    positions.Add(new SharpDXVector3(0, 0, 0));
                    colors.Add(new Color4(1.0f, 0.0f, 0.0f, 1.0f)); // 红色，完全不透明
                    Logger.Info("已清除所有测量");
                }

                // 创建几何体
                PointGeometry.Positions = positions;
                PointGeometry.Colors = colors;

                // 更新点大小（PointGeometryModel3D.Size 是 System.Windows.Size 类型）
                // FixedSize="True" 确保点大小不受相机距离影响
                PointModel.Size = new System.Windows.Size(_pointSize, _pointSize);
                
                // 确保PointModel可见且启用
                PointModel.Visibility = System.Windows.Visibility.Visible;
                PointModel.IsRendering = true;
                
                // 刷新几何体
                PointGeometry.UpdateVertices();
                
                Logger.Info($"几何体更新完成: {positions.Count} 点, {colors.Count} 颜色");

                if (!_cameraInitialized || resetCamera)
                {
                    InitializeCameraFromBBox(bbox);
                    _cameraInitialized = true;
                }
                StatusText.Text = $"已加载：{cloud.Count} 点（渲染 {pointsToRender.Count} 点，偏移 [{offsetX:F2}, {offsetY:F2}, {offsetZ:F2}]）[GPU硬件加速]";
            }
            catch (Exception ex)
            {
                Logger.Error("更新点云失败", ex);
                MessageBox.Show($"更新点云失败：{ex.Message}\n\n提示：如果看到黑屏，请检查：\n1. 坐标是否过大（UTM坐标需要归一化）\n2. Alpha通道是否为1.0\n3. 是否调用了ZoomExtents()", "错误");
            }
        }

        /// <summary>更新 Helix 相机位置</summary>
        /// <remarks>
        /// 注意：由于点云已经去中心化（移到原点附近），
        /// 相机应该看向原点 (0,0,0) 附近
        /// </remarks>
        private void UpdateHelixCamera(PointCloudBBox bbox)
        {
            if (HelixCamera == null) return;

            // 计算包围盒大小（去中心化后的尺寸）
            var sizeX = bbox.MaxX - bbox.MinX;
            var sizeY = bbox.MaxY - bbox.MinY;
            var sizeZ = bbox.MaxZ - bbox.MinZ;
            var maxSize = Math.Max(sizeX, Math.Max(sizeY, sizeZ));

            // 由于点云已经去中心化到原点附近，相机应该看向原点
            // 计算相机位置（从斜上方观察原点）
            var distance = maxSize * 1.5f;
            var cameraPos = new WpfVector3(
                distance * 0.7f,
                distance * 0.7f,
                distance * 0.7f);

            HelixCamera.Position = new System.Windows.Media.Media3D.Point3D(cameraPos.X, cameraPos.Y, cameraPos.Z);
            // 看向原点
            HelixCamera.LookDirection = new System.Windows.Media.Media3D.Vector3D(
                -cameraPos.X,
                -cameraPos.Y,
                -cameraPos.Z);
            HelixCamera.UpDirection = new System.Windows.Media.Media3D.Vector3D(0, 1, 0);
        }

        private void InitializeCameraFromBBox(PointCloudBBox bbox)
        {
            if (HelixCamera == null) return;
            var sizeX = bbox.MaxX - bbox.MinX;
            var sizeY = bbox.MaxY - bbox.MinY;
            var sizeZ = bbox.MaxZ - bbox.MinZ;
            var maxSize = Math.Max(sizeX, Math.Max(sizeY, sizeZ));
            _cameraDistance = Math.Max(2.0, maxSize * 1.5);
            _cameraTarget = System.Numerics.Vector3.Zero;
            _cameraYaw = -45;
            _cameraPitch = -25;
            Logger.Info($"初始化相机: BBox Size={maxSize:F2}, Distance={_cameraDistance:F2}");
            ApplyCameraTransform();
        }

        private void ApplyCameraTransform()
        {
            if (HelixCamera == null) return;
            double yawRad = _cameraYaw * Math.PI / 180.0;
            double pitchRad = _cameraPitch * Math.PI / 180.0;
            var dir = new System.Numerics.Vector3(
                (float)(Math.Cos(pitchRad) * Math.Cos(yawRad)),
                (float)(Math.Sin(pitchRad)),
                (float)(Math.Cos(pitchRad) * Math.Sin(yawRad)));

            var position = _cameraTarget - dir * (float)_cameraDistance;
            HelixCamera.Position = new System.Windows.Media.Media3D.Point3D(position.X, position.Y, position.Z);

            var look = _cameraTarget - position;
            HelixCamera.LookDirection = new System.Windows.Media.Media3D.Vector3D(look.X, look.Y, look.Z);
            HelixCamera.UpDirection = new System.Windows.Media.Media3D.Vector3D(0, 1, 0);
            
            // 调试日志（简化版，减少日志量）
            // Logger.Info($"相机状态: Target={_cameraTarget}, Yaw={_cameraYaw:F1}°, Pitch={_cameraPitch:F1}°, Distance={_cameraDistance:F2}");
        }

        private void RotateCamera(WpfVector delta)
        {
            _cameraYaw += delta.X * RotateSpeed;
            _cameraPitch -= delta.Y * RotateSpeed;
            // 放宽俯仰范围，避免“被挡住”感觉
            _cameraPitch = Math.Clamp(_cameraPitch, -179, 179);
            ApplyCameraTransform();
        }

        private void PanCamera(WpfVector delta)
        {
            double scale = Math.Max(0.001, _cameraDistance * PanSpeed);
            var basis = GetViewBasis();
            _cameraTarget += basis.right * (float)(-delta.X * scale);
            _cameraTarget += basis.up * (float)(delta.Y * scale);
            ApplyCameraTransform();
        }

        private void ZoomCamera(int delta)
        {
            if (delta == 0) return;
            double oldDistance = _cameraDistance;
            double factor = delta > 0 ? 1 / ZoomFactor : ZoomFactor;
            _cameraDistance = Math.Clamp(_cameraDistance * factor, 0.05, 1_000_000);
            Logger.Info($"滚轮缩放: Delta={delta}, Factor={factor:F4}, Distance {oldDistance:F2} → {_cameraDistance:F2}");
            ApplyCameraTransform();
        }

        private (System.Numerics.Vector3 forward, System.Numerics.Vector3 right, System.Numerics.Vector3 up) GetViewBasis()
        {
            double yawRad = _cameraYaw * Math.PI / 180.0;
            double pitchRad = _cameraPitch * Math.PI / 180.0;
            var forward = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(
                (float)(Math.Cos(pitchRad) * Math.Cos(yawRad)),
                (float)(Math.Sin(pitchRad)),
                (float)(Math.Cos(pitchRad) * Math.Sin(yawRad))));
            var worldUp = System.Numerics.Vector3.UnitY;
            var right = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(forward, worldUp));
            var up = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(right, forward));
            return (forward, right, up);
        }

        private void MoveCameraRelative(double forward, double right, double vertical, double multiplier)
        {
            double baseStep = Math.Max(0.001, _cameraDistance * KeyboardMoveSpeed);
            double step = baseStep * multiplier;
            
            // W/S/A/D 只在水平面（XZ平面）上移动，不改变高度
            // 计算水平前向方向（去掉Y分量）
            double yawRad = _cameraYaw * Math.PI / 180.0;
            var horizontalForward = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(
                (float)Math.Cos(yawRad),
                0,  // Y分量为0，保持在水平面
                (float)Math.Sin(yawRad)));
            var horizontalRight = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(
                (float)Math.Sin(yawRad),
                0,  // Y分量为0，保持在水平面
                -(float)Math.Cos(yawRad)));
            
            var oldTarget = _cameraTarget;
            _cameraTarget += horizontalForward * (float)(forward * step);
            _cameraTarget += horizontalRight * (float)(right * step);
            _cameraTarget += System.Numerics.Vector3.UnitY * (float)(vertical * step);  // Q/E 控制垂直移动
            
            Logger.Info($"移动相机: F={forward:F1}, R={right:F1}, V={vertical:F1}, Step={step:F4}, TargetΔ={_cameraTarget - oldTarget}");
            ApplyCameraTransform();
        }

        private async void OpenPointCloud_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            { 
                Filter = "所有文件|*.*|LAS 文件 (*.las)|*.las|XYZ 文件 (*.xyz)|*.xyz|PLY 文件 (*.ply)|*.ply",
                FilterIndex = 1 // 默认选择第一个（所有文件）
            };
            if (ofd.ShowDialog() == true)
            {
                try
                {
                    Logger.Info($"开始读取点云文件: {ofd.FileName}");
                    var reader = _readers.FirstOrDefault(r => r.CanRead(System.IO.Path.GetExtension(ofd.FileName)));
                    if (reader == null) 
                    { 
                        MessageBox.Show("不支持的格式");
                        Logger.Warning($"不支持的格式: {System.IO.Path.GetExtension(ofd.FileName)}");
                        return; 
                    }
                    StatusText.Text = $"读取中：{reader.Name}";
                    
                    // 如果是流式读取器，显示进度
                    PointCloud cloud;
                    if (reader is IO.StreamingLasReader streamingReader)
                    {
                        var progress = new Progress<double>(p => 
                        {
                            Dispatcher.Invoke(() => StatusText.Text = $"读取中：{reader.Name} - {p:F1}%");
                        });
                        cloud = await streamingReader.ReadAsync(ofd.FileName, CancellationToken.None, progress);
                    }
                    else
                    {
                        cloud = await reader.ReadAsync(ofd.FileName, CancellationToken.None);
                    }
                    _original = cloud;
                    _cloud = new PointCloud(cloud.Points);
                    ClearMeasurementsAndVisuals();
                    _vm.PointCount = _cloud.Count;
                    CurrentFileText.Text = ofd.FileName;
                    BBoxText.Text = _cloud.BBox.ToString();
                    PointCountText.Text = _cloud.Count.ToString();
                    Logger.Info($"点云加载完成: {_cloud.Count} 点");
                    UpdatePointCloudGPU(_cloud, resetCamera: true);
                    PromptVoxelDownsampleIfLarge();
                }
                catch (Exception ex)
                {
                    Logger.Error("读取点云文件失败", ex);
                    MessageBox.Show(ex.Message, "读取失败");
                }
            }
        }

        private async void SaveAsXyz_Click(object sender, RoutedEventArgs e)
        {
            if (_cloud == null) return;
            var sfd = new SaveFileDialog{ Filter = "XYZ|*.xyz" };
            if (sfd.ShowDialog() == true)
            {
                await XyzWriter.WriteAsync(_cloud, sfd.FileName);
                StatusText.Text = "已清除所有测量";
            }
        }

        private async void SaveProject_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog{ Filter = "项目|*.json" };
            if (sfd.ShowDialog() == true)
            {
                var s = new ProjectSettings
                {
                    DataFile = CurrentFileText.Text,
                    ColorMap = _colorMap is HeightColorMap ? "Height" : "Intensity",
                    PointSize = _pointSize,
                    Background = _background == Colors.Black ? "Black" : _background == Colors.White ? "White" : "Gray",
                    // 注意：Helix Toolkit 使用自己的相机系统，相机状态由 Viewport3DX 自动管理
                    CameraYaw = 0, CameraPitch = 0, CameraDistance = 0,
                    CameraTargetX = 0, CameraTargetY = 0, CameraTargetZ = 0
                };
                await ProjectIO.SaveAsync(sfd.FileName, s);
                StatusText.Text = "已清除所有测量";
            }
        }

        private async void OpenProject_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog{ Filter = "项目|*.json" };
            if (ofd.ShowDialog() == true)
            {
                var s = await ProjectIO.LoadAsync(ofd.FileName);
                if (s == null) return;
                if (!string.IsNullOrEmpty(s.DataFile))
                {
                    var reader = _readers.FirstOrDefault(r => r.CanRead(System.IO.Path.GetExtension(s.DataFile)));
                    if (reader != null && System.IO.File.Exists(s.DataFile))
                    {
                        _original = await reader.ReadAsync(s.DataFile, CancellationToken.None);
                        _cloud = new PointCloud(_original.Points);
                        ClearMeasurementsAndVisuals();
                        _vm.PointCount = _cloud.Count;
                        CurrentFileText.Text = s.DataFile;
                        BBoxText.Text = _cloud.BBox.ToString();
                        PointCountText.Text = _cloud.Count.ToString();
                    }
                }
                _colorMap = s.ColorMap == "Intensity" ? new IntensityColorMap() : new HeightColorMap();
                _pointSize = s.PointSize;
                _background = s.Background == "White" ? Colors.White : s.Background == "Gray" ? Colors.Gray : Colors.Black;
                // 注意：Helix 相机控制由 Viewport3DX 自动处理，不需要手动设置
                if (_cloud != null)
                {
                    UpdatePointCloudGPU(_cloud, resetCamera: true);
                    PromptVoxelDownsampleIfLarge();
                }
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e) => Close();
        private void ColorMapHeight_Click(object sender, RoutedEventArgs e) { _colorMap = new HeightColorMap(); if (_cloud != null) UpdatePointCloudGPU(_cloud); }
        private void ColorMapIntensity_Click(object sender, RoutedEventArgs e) { _colorMap = new IntensityColorMap(); if (_cloud != null) UpdatePointCloudGPU(_cloud); }
        private void BackgroundBlack_Click(object sender, RoutedEventArgs e) 
        { 
            _background = Colors.Black; 
            if (Viewport3D != null) 
            {
                // Viewport3D.BackgroundColor 需要 System.Windows.Media.Color 类型
                Viewport3D.BackgroundColor = Colors.Black;
            }
        }
        private void BackgroundWhite_Click(object sender, RoutedEventArgs e) 
        { 
            _background = Colors.White; 
            if (Viewport3D != null) 
            {
                Viewport3D.BackgroundColor = Colors.White;
            }
        }
        private void BackgroundGray_Click(object sender, RoutedEventArgs e) 
        { 
            _background = Colors.Gray; 
            if (Viewport3D != null) 
            {
                Viewport3D.BackgroundColor = Colors.Gray;
            }
        }
        private void ResetView_Click(object sender, RoutedEventArgs e) 
        { 
            if (_cloud != null) 
            { 
                InitializeCameraFromBBox(_cloud.BBox);
                _cameraInitialized = true;
                ApplyCameraTransform();
            } 
        }

        private async void FilterZ_Click(object sender, RoutedEventArgs e)
        {
            if (_cloud == null) return;
            var dlg = new SimpleTwoInputDialog("Z最小值：", "Z最大值：", _cloud.BBox.MinZ.ToString("F2"), _cloud.BBox.MaxZ.ToString("F2"));
            if (dlg.ShowDialog() == true)
            {
                if (float.TryParse(dlg.Value1, out float minZ) && float.TryParse(dlg.Value2, out float maxZ))
                {
                    var f = new RangeFilter(minZ, maxZ);
                    var filtered = f.Apply(_original?.Points ?? _cloud.Points, _cloud.BBox);
                    _cloud = new PointCloud(filtered);
                    ClearMeasurementsAndVisuals();
                    _vm.PointCount = _cloud.Count;
                    PointCountText.Text = _cloud.Count.ToString();
                    UpdatePointCloudGPU(_cloud);
                }
            }
        }

        private void VoxelDownsample_Click(object sender, RoutedEventArgs e)
        {
            if (_cloud == null) return;
            var dlg = new SimpleOneInputDialog("体素大小（建议0.1~1.0）：", "0.2");
            if (dlg.ShowDialog() == true && float.TryParse(dlg.Value, out float vox))
            {
                ApplyVoxelDownsample(vox);
            }
        }

        private void ApplyVoxelDownsample(float vox)
        {
            if (_cloud == null) return;
            vox = Math.Clamp(vox, 0.01f, 5f);
            Logger.Info($"执行体素下采样: 大小={vox}");
            var original = _cloud;
            var command = new VoxelDownsampleCommand(
                original,
                vox,
                (cloud) =>
                {
                    _cloud = cloud;
                    _vm.PointCount = cloud.Count;
                    PointCountText.Text = cloud.Count.ToString();
                    UpdatePointCloudGPU(cloud);
                },
                (cloud) =>
                {
                    _cloud = cloud;
                    _vm.PointCount = cloud.Count;
                    PointCountText.Text = cloud.Count.ToString();
                    UpdatePointCloudGPU(cloud);
                }
            );
            _commandManager.Execute(command);
            Logger.Info($"体素下采样完成: {_cloud.Count} 点");
        }

        private void PromptVoxelDownsampleIfLarge()
        {
            if (_cloud == null) return;
            if (_cloud.Count < LargePointPromptThreshold) return;

            var result = MessageBox.Show(
                $"检测到大点云：{_cloud.Count} 点。\n是否立即进行体素下采样以提升性能？\n推荐体素大小：0.2~0.5",
                "性能提示",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                ApplyVoxelDownsample(0.2f);
            }
        }
        private async void RadiusOutlier_Click(object sender, RoutedEventArgs e)
        {
            if (_cloud == null) return;
            var dlg = new SimpleTwoInputDialog("邻域半径：", "最少邻居数：", "0.5", "6");
            if (dlg.ShowDialog() == true && float.TryParse(dlg.Value1, out float r) && int.TryParse(dlg.Value2, out int k))
            {
                var f = new RadiusOutlierFilter(r, k);
                var filtered = f.Apply(_cloud.Points, _cloud.BBox);
                _cloud = new PointCloud(filtered);
                ClearMeasurementsAndVisuals();
                _vm.PointCount = _cloud.Count;
                PointCountText.Text = _cloud.Count.ToString();
                UpdatePointCloudGPU(_cloud);
            }
        }

        private void RestoreOriginal_Click(object sender, RoutedEventArgs e)
        {
            if (_original == null) return;
            _cloud = new PointCloud(_original.Points);
            ClearMeasurementsAndVisuals();
            _vm.PointCount = _cloud.Count;
            PointCountText.Text = _cloud.Count.ToString();
            UpdatePointCloudGPU(_cloud, resetCamera: true);
            PromptVoxelDownsampleIfLarge();
        }

        private void ShowStats_Click(object sender, RoutedEventArgs e)
        {
            if (_cloud == null) return;
            var s = _cloud.StatsZ();
            MessageBox.Show($"Count: {s.Count}\\nMean Z: {s.MeanZ:F3}\\nRange: [{s.MinZ:F3}, {s.MaxZ:F3}]", "Stats");
        }

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            var helpText = @"Point Cloud Viewer - Help

[Basics]
- Left drag: rotate
- Right drag: pan
- Wheel/Middle drag: zoom
- Alt + Left: temp rotate in measure mode
- WASD move, Q/E up-down

[File]
- Open: XYZ / PLY / LAS
- Save: XYZ
- Save/Open project: view + file path

[View]
- Color map: height / intensity
- Background: black / white / gray
- Point size: slider 1-8
- Reset view

[Filter]
- Z range filter
- Voxel downsample
- Radius outlier removal
- Restore original; undo/redo

[Measurement]
- Distance: left-click 2 points (Alt+Left rotate)
- Area: left-click 3+ points, auto polygon area
- Leaving measure mode or loading new cloud clears results

[Performance]
- GPU/CPU render fallback
- Octree/LOD/tiling
- Large clouds: voxel downsample first

[Requirements]
- .NET 8.0+
- Windows 10/11

Log: PointCloudApp.log";

            var helpWindow = new Window
            {
                Title = "Help",
                Width = 600,
                Height = 700,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.CanResize
            };

            var scrollViewer = new System.Windows.Controls.ScrollViewer
            {
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                Padding = new Thickness(15)
            };

            var textBlock = new System.Windows.Controls.TextBlock
            {
                Text = helpText,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas, Segoe UI"),
                FontSize = 13,
                LineHeight = 22
            };

            scrollViewer.Content = textBlock;
            helpWindow.Content = scrollViewer;
            helpWindow.ShowDialog();
        }


        private void About_Click(object sender, RoutedEventArgs e)
        {
            var aboutText = @"Point Cloud Viewer (Basic)
Final Project

Author: SuLiverse

Major: Surveying Engineering

Features:
- GPU/CPU rendering
- Octree spatial index
- Adaptive LOD
- Interactive measurement
- Chunked compression
- Undo/Redo";

            MessageBox.Show(aboutText, "About");
        }

        /// <summary>测试渲染功能（用于调试黑屏问题）</summary>
        private void TestRender_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Logger.Info("已清除所有测量");
                
                // 创建一个简单的测试点云（在原点附近的立方体）
                var testPoints = new List<PointRecord>();
                int gridSize = 10;
                for (int x = -gridSize; x <= gridSize; x++)
                {
                    for (int y = -gridSize; y <= gridSize; y++)
                    {
                        for (int z = -gridSize; z <= gridSize; z++)
                        {
                            // 只创建立方体的边框
                            if (Math.Abs(x) == gridSize || Math.Abs(y) == gridSize || Math.Abs(z) == gridSize)
                            {
                                byte r = (byte)((x + gridSize) * 255 / (2 * gridSize));
                                byte g = (byte)((y + gridSize) * 255 / (2 * gridSize));
                                byte b = (byte)((z + gridSize) * 255 / (2 * gridSize));
                                
                                testPoints.Add(new PointRecord
                                {
                                    X = x * 1.0f,
                                    Y = y * 1.0f,
                                    Z = z * 1.0f,
                                    Color = WpfColor.FromRgb(r, g, b),
                                    Intensity = 100
                                });
                            }
                        }
                    }
                }
                
                Logger.Info($"创建测试点云: {testPoints.Count} 点");
                
                // 创建测试点云对象
                _cloud = new PointCloud(testPoints);
                ClearMeasurementsAndVisuals();
                _original = _cloud;
                _vm.PointCount = _cloud.Count;
                CurrentFileText.Text = "(测试点云 - 彩色立方体)";
                BBoxText.Text = _cloud.BBox.ToString();
                PointCountText.Text = _cloud.Count.ToString();
                
                // 渲染测试点云
                UpdatePointCloudGPU(_cloud);
                
                StatusText.Text = $"测试点云已加载：{testPoints.Count} 点（如果看到彩色立方体，说明渲染正常）";
                MessageBox.Show(
                    $"测试点云已创建！\n\n" +
                    $"点数：{testPoints.Count}\n" +
                    $"位置：原点附近的彩色立方体\n" +
                    $"范围：{_cloud.BBox}\n\n" +
                    $"如果看到彩色立方体，说明渲染引擎工作正常。\n" +
                    $"如果看不到，可能是：\n" +
                    $"1. 显卡驱动问题\n" +
                    $"2. DirectX版本不兼容\n" +
                    $"3. EffectsManager初始化失败",
                    "测试渲染");
            }
            catch (Exception ex)
            {
                Logger.Error("测试渲染失败", ex);
                MessageBox.Show($"测试渲染失败：{ex.Message}", "错误");
            }
        }

        private void MeasurementDistance_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem item)
            {
                if (item.IsChecked)
                {
                    // 互斥：关闭面积模式
                    var areaItem = FindName("MeasurementAreaMenuItem") as System.Windows.Controls.MenuItem;
                    if (areaItem != null) areaItem.IsChecked = false;

                    _measurementTool.IsActive = true;
                    _measurementTool.Mode = MeasurementMode.Distance;
                    StatusText.Text = "测距模式：左键拾取两点测距（Alt+左键旋转）";
                    Logger.Info("测距模式开启");
                }
                else
                {
                    _measurementTool.IsActive = false;
                    _measurementTool.Mode = MeasurementMode.None;
                    ClearMeasurementsAndVisuals();
                    StatusText.Text = "测距模式关闭";
                    Logger.Info("测距模式关闭");
                }
            }
        }

        private void MeasurementArea_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem item)
            {
                if (item.IsChecked)
                {
                    // 互斥：关闭测距模式
                    var distItem = FindName("MeasurementDistanceMenuItem") as System.Windows.Controls.MenuItem;
                    if (distItem != null) distItem.IsChecked = false;

                    _measurementTool.IsActive = true;
                    _measurementTool.Mode = MeasurementMode.Area;
                    StatusText.Text = "测面积模式：左键连续拾取3个及以上点自动计算任意多边形面积";
                    Logger.Info("测面积模式开启");
                }
                else
                {
                    _measurementTool.IsActive = false;
                    _measurementTool.Mode = MeasurementMode.None;
                    ClearMeasurementsAndVisuals();
                    StatusText.Text = "测面积模式关闭";
                    Logger.Info("测面积模式关闭");
                }
            }
        }

private void Viewport3D_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (Viewport3D == null || HelixCamera == null) return;
            
            // 保存当前相机状态
            var savedTarget = _cameraTarget;
            var savedYaw = _cameraYaw;
            var savedPitch = _cameraPitch;
            var savedDistance = _cameraDistance;
            
            Logger.Info($"鼠标按下: Button={e.ChangedButton}, Target={_cameraTarget}, Yaw={_cameraYaw:F1}°, Pitch={_cameraPitch:F1}°, Distance={_cameraDistance:F2}");
            
            _lastMousePos = e.GetPosition(Viewport3D);

            bool altDown = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
            bool handledMeasurement = false;

            if (_measurementTool.IsActive && e.ChangedButton == MouseButton.Left && !altDown)
            {
                handledMeasurement = HandleMeasurementClick(_lastMousePos);
            }

            if (handledMeasurement)
            {
                e.Handled = true;
                return;
            }

            if (e.ChangedButton == MouseButton.Left)
            {
                _isRotating = true;
                Viewport3D.CaptureMouse();
                e.Handled = true;
            }
            else if (e.ChangedButton == MouseButton.Right)
            {
                _isPanning = true;
                Viewport3D.CaptureMouse();
                e.Handled = true;
            }
            
            // 强制恢复相机状态，防止 HelixToolkit 干扰
            _cameraTarget = savedTarget;
            _cameraYaw = savedYaw;
            _cameraPitch = savedPitch;
            _cameraDistance = savedDistance;
            
            // 立即应用相机变换
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ApplyCameraTransform();
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        private void Viewport3D_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (Viewport3D == null) return;
            if (!_isRotating && !_isPanning) return;

            var pos = e.GetPosition(Viewport3D);
            WpfVector delta = pos - _lastMousePos;
            _lastMousePos = pos;

            if (_isRotating)
            {
                RotateCamera(delta);
                e.Handled = true;
            }
            else if (_isPanning)
            {
                PanCamera(delta);
                e.Handled = true;
            }
        }

        private void Viewport3D_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (Viewport3D == null || HelixCamera == null) return;
            
            // 保存当前相机状态
            var savedTarget = _cameraTarget;
            var savedYaw = _cameraYaw;
            var savedPitch = _cameraPitch;
            var savedDistance = _cameraDistance;
            
            Logger.Info($"鼠标释放: Button={e.ChangedButton}, Target={_cameraTarget}, Distance={_cameraDistance:F2}, CameraPos={HelixCamera.Position}");

            if (e.ChangedButton == MouseButton.Left && _isRotating)
            {
                _isRotating = false;
                Viewport3D.ReleaseMouseCapture();
                e.Handled = true;
            }
            else if (e.ChangedButton == MouseButton.Right && _isPanning)
            {
                _isPanning = false;
                Viewport3D.ReleaseMouseCapture();
                e.Handled = true;
            }
            
            // 强制恢复相机状态，防止 HelixToolkit 干扰
            _cameraTarget = savedTarget;
            _cameraYaw = savedYaw;
            _cameraPitch = savedPitch;
            _cameraDistance = savedDistance;
            
            // 立即应用相机变换
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ApplyCameraTransform();
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        private void Viewport3D_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            Logger.Info($"PreviewMouseWheel 触发: Delta={e.Delta}");
            ZoomCamera(e.Delta);
            e.Handled = true;
        }

        private void Viewport3D_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            Logger.Info($"MouseWheel 触发: Delta={e.Delta}");
            ZoomCamera(e.Delta);
            e.Handled = true;
        }

        private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            // 全局滚轮捕获，确保一定能接收到
            if (Viewport3D != null && Viewport3D.IsMouseOver)
            {
                Logger.Info($"Window_MouseWheel 触发: Delta={e.Delta}");
                ZoomCamera(e.Delta);
                e.Handled = true;
            }
        }

        private void SyncCameraState(object? sender, EventArgs e)
        {
            if (HelixCamera == null) return;
            
            // 从 HelixCamera 的实际位置反推 Distance
            var cameraPos = HelixCamera.Position;
            var cameraPosVec = new System.Numerics.Vector3(
                (float)cameraPos.X, 
                (float)cameraPos.Y, 
                (float)cameraPos.Z);
            
            var actualDistance = System.Numerics.Vector3.Distance(cameraPosVec, _cameraTarget);
            
            // 如果距离变化超过阈值（说明被外部修改了），同步到我们的变量
            if (Math.Abs(actualDistance - _cameraDistance) > 0.1)
            {
                _cameraDistance = actualDistance;
                Logger.Info($"相机同步: Distance 被外部修改为 {_cameraDistance:F2}");
            }
        }

        private SharpDXVector3 ToRenderSpace(System.Numerics.Vector3 p)
        {
            return new SharpDXVector3(
                p.X - _renderOffset.X,
                p.Y - _renderOffset.Y,
                p.Z - _renderOffset.Z);
        }

        private void UpdateMeasurementVisuals()
        {
            if (MeasurementLineModel == null || MeasurementHelperLineModel == null || MeasurementMarkerModel == null)
                return;

            var lineBuilder = new LineBuilder();
            var helperBuilder = new LineBuilder();
            var markerPositions = new Vector3Collection();

            foreach (var measurement in _measurementTool.Measurements)
            {
                if (measurement.Points == null || measurement.Points.Count == 0) continue;
                var renderPts = measurement.Points.Select(ToRenderSpace).ToList();

                foreach (var p in renderPts)
                    markerPositions.Add(p);

                if (measurement.Type == MeasurementType.Distance && renderPts.Count >= 2)
                {
                    lineBuilder.AddLine(renderPts[0], renderPts[1]);
                }
                else if (measurement.Type == MeasurementType.Area && renderPts.Count >= 3)
                {
                    for (int i = 0; i < renderPts.Count; i++)
                    {
                        var a = renderPts[i];
                        var b = renderPts[(i + 1) % renderPts.Count];
                        lineBuilder.AddLine(a, b);
                    }
                }
            }

            var selected = _measurementTool.SelectedPoints;
            if (selected.Count > 0)
            {
                var renderSelected = selected.Select(ToRenderSpace).ToList();
                foreach (var p in renderSelected)
                    markerPositions.Add(p);

                if (renderSelected.Count >= 2)
                {
                    for (int i = 0; i < renderSelected.Count - 1; i++)
                    {
                        helperBuilder.AddLine(renderSelected[i], renderSelected[i + 1]);
                    }
                }
            }

            MeasurementMarkerModel.Geometry = new PointGeometry3D
            {
                Positions = markerPositions
            };
            MeasurementLineModel.Geometry = lineBuilder.ToLineGeometry3D();
            MeasurementHelperLineModel.Geometry = helperBuilder.ToLineGeometry3D();
        }

        private void ClearMeasurementsAndVisuals()
        {
            _measurementTool.ClearAll();
            UpdateMeasurementVisuals();
        }

        private Camera? CreateMeasurementCamera()
        {
            if (HelixCamera == null)
                return null;

            var look = HelixCamera.LookDirection;
            var dir = new System.Numerics.Vector3((float)look.X, (float)look.Y, (float)look.Z);
            var distance = dir.Length();
            if (distance < 1e-4f)
                return null;

            var dirNormalized = System.Numerics.Vector3.Normalize(dir);
            float yaw = (float)(Math.Atan2(dirNormalized.Z, dirNormalized.X) * 180.0 / Math.PI);
            float pitch = (float)(Math.Asin(dirNormalized.Y) * 180.0 / Math.PI);

            var position = HelixCamera.Position;
            var cameraPos = new System.Numerics.Vector3((float)position.X, (float)position.Y, (float)position.Z) + _renderOffset;
            var target = cameraPos + dir;

            return new Camera
            {
                Target = target,
                Distance = distance,
                Yaw = yaw,
                Pitch = pitch,
                Near = (float)HelixCamera.NearPlaneDistance,
                Far = (float)HelixCamera.FarPlaneDistance,
                Fov = (float)(HelixCamera.FieldOfView * Math.PI / 180.0)
            };
        }

        private bool HandleMeasurementClick(WpfPoint position)
        {
            if (_cloud == null || Viewport3D == null)
                return false;

            var camera = CreateMeasurementCamera();
            if (camera == null)
                return false;

            int width = (int)Math.Max(1, Viewport3D.ActualWidth);
            int height = (int)Math.Max(1, Viewport3D.ActualHeight);
            bool ok = _measurementTool.OnMouseClick(position, camera, _cloud, width, height);
            UpdateMeasurementVisuals();
            return ok;
        }

        private void MainWindow_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            if (_cloud == null)
                return;

            double multiplier = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 3.0 : 1.0;
            bool handled = true;

            switch (e.Key)
            {
                case Key.W:
                    MoveCameraRelative(1, 0, 0, multiplier);
                    break;
                case Key.S:
                    MoveCameraRelative(-1, 0, 0, multiplier);
                    break;
                case Key.A:
                    MoveCameraRelative(0, -1, 0, multiplier);
                    break;
                case Key.D:
                    MoveCameraRelative(0, 1, 0, multiplier);
                    break;
                case Key.Q:
                    MoveCameraRelative(0, 0, 1, multiplier);
                    break;
                case Key.E:
                    MoveCameraRelative(0, 0, -1, multiplier);
                    break;
                case Key.R:
                    if (_cloud != null)
                    {
                        InitializeCameraFromBBox(_cloud.BBox);
                        _cameraInitialized = true;
                        ApplyCameraTransform();
                    }
                    break;
                default:
                    handled = false;
                    break;
            }

            if (handled)
            {
                e.Handled = true;
            }
        }

                        private void ClearMeasurements_Click(object sender, RoutedEventArgs e)
        {
            _measurementTool.ClearAll();
            StatusText.Text = "Measurements cleared";
            Logger.Info("Measurements cleared");
            UpdateMeasurementVisuals();
        }

private void ToggleLod_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem item)
            {
                _lodEnabled = item.IsChecked;
                StatusText.Text = _lodEnabled ? "动态LOD已启用" : "动态LOD已禁用";
                Logger.Info($"动态LOD: {(_lodEnabled ? "启用" : "禁用")}");
                if (_cloud != null)
                {
                    UpdatePointCloudGPU(_cloud);
                }
            }
        }

        private void PointSizeSlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            _pointSize = (int)e.NewValue;
            if (PointModel != null)
            {
                // PointGeometryModel3D.Size 是 System.Windows.Size 类型（宽度和高度）
                PointModel.Size = new System.Windows.Size(_pointSize, _pointSize);
            }
        }

        // 注意：Helix Toolkit 的 Viewport3DX 已经内置了鼠标交互（旋转、平移、缩放）
        // 不需要手动实现这些功能

        // 新增：生成合成街景点云
        private async void GenerateSynthetic_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                Title = "保存生成的街景点云",
                Filter = "PLY 点云 (*.ply)|*.ply|XYZ 点云 (*.xyz)|*.xyz",
                FileName = "street_nav"
            };
            if (sfd.ShowDialog() != true) return;

            var opt = new StreetGenOptions
            {
                RoadLength = 80,
                RoadWidth  = 10,
                Step       = 0.08,
                EnableBuildings = true,
                EnableCars  = true,
                EnableTrees = true,
                EnablePoles = true,
                CarCount    = 6,
                TreeCount   = 8,
                PoleCount   = 6,
                Seed        = 2025
            };

            StatusText.Text = "已清除所有测量";
            var progress = new Progress<double>(p => StatusText.Text = $"生成中... {(int)(p * 100)}%");

            try
            {
                await StreetGenerator.GenerateAsync(sfd.FileName, opt, CancellationToken.None, progress);
                StatusText.Text = "已清除所有测量";

                var reader = _readers.FirstOrDefault(r => r.CanRead(System.IO.Path.GetExtension(sfd.FileName)));
                if (reader == null)
                {
                    MessageBox.Show("生成完成，但当前格式无读取器。请确认已实现 PlyReader / XyzReader。", "提示");
                    StatusText.Text = "已清除所有测量";
                    return;
                }

                _original = await reader.ReadAsync(sfd.FileName, CancellationToken.None);
                _cloud = new PointCloud(_original.Points);
                ClearMeasurementsAndVisuals();
                _vm.PointCount = _cloud.Count;
                CurrentFileText.Text = sfd.FileName;
                BBoxText.Text = _cloud.BBox.ToString();
                PointCountText.Text = _cloud.Count.ToString();

                UpdatePointCloudGPU(_cloud, resetCamera: true);
                StatusText.Text = $"完成：{_cloud.Count} 点";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "生成失败");
                StatusText.Text = "已清除所有测量";
            }
        }
    }
}
