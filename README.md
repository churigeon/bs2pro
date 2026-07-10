# BS2 Lite

<p align="center">
  <img src="assets/BS2Lite.png" width="128" alt="BS2 Lite icon">
</p>

面向飞智 BS2 Pro 压风式散热底座的轻量 Windows 后台控制器。发布版本是一个自包含的 `BS2Lite.exe`：无需安装 .NET、无需附带 DLL，也不会启动飞智空间站。

## 功能

- 单文件便携版，可放在任意文件夹
- 智能变频：静音、性能、自定义 8 点温控曲线
- 固定转速：12 档，1300–4000 rpm
- 自动识别 PD 20V / QC / 5V 对应的最高转速
- 档位灯、智能启停、底座通电自启动
- 断线自动重连，重连后恢复上次配置
- 登录 Windows 后静默在托盘运行
- 原生 WinExe 入口，无黑色控制台窗口
- 适配高 DPI / 200% Windows 缩放

RGB 灯光颜色、亮度和动画不包含在 Lite 中；连接时 RGB 保持关闭。

## 使用便携版

1. 将 `BS2Lite.exe` 放到准备长期使用的位置。
2. 双击一次，打开控制面板并自动登记当前路径为开机自启。
3. 设置完成后点击“保存并应用”，以后可直接关闭面板，程序会留在托盘。

移动文件时，先从托盘菜单退出 BS2 Lite，再移动 EXE，然后从新位置双击一次。程序会自动把开机启动项更新到新路径，原配置不会丢失。

配置、状态和日志保存在 `%LOCALAPPDATA%\FlydigiBS2Lite`，因此移动或替换 EXE 不会影响已经保存的散热设置。

默认性能曲线：

| 温度 | 30°C | 40°C | 50°C | 60°C | 70°C | 80°C | 90°C | 100°C |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 转速 | 1700 | 1700 | 2100 | 2700 | 3300 | 3500 | 4000 | 4000 |

## 构建

要求：Windows 10/11 和 .NET 8 SDK。

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

构建结果位于 `artifacts\publish\BS2Lite.exe`，目录内只有这一个发布文件。程序以 `win-x64` 自包含单文件方式发布，依赖首次启动时由 .NET 单文件宿主自动解包到系统临时目录。

项目通过 `Microsoft.Windows.SDK.NET` 使用 WinRT GATT，并通过 `LibreHardwareMonitorLib` 读取 CPU/GPU 温度。当前设备蓝牙地址位于 `src/BS2Lite/Program.cs` 的 `DeviceAddress` 常量中；迁移到另一台底座时需要修改该地址并重新构建。

## 说明

本项目是针对个人硬件协议分析后制作的非官方工具，与飞智官方无关联。请保留官方软件，以便固件升级或故障排查。
