# BS2 Lite

<p align="center">
  <img src="assets/BS2Lite.png" width="128" alt="BS2 Lite icon">
</p>

面向飞智 BS2 Pro 压风式散热底座的轻量 Windows 后台控制器。它不启动飞智空间站，登录 Windows 后直接连接已经配对的底座、恢复上次配置，并常驻系统托盘。

## 功能

- 智能变频：静音、性能、自定义 8 点温控曲线
- 固定转速：12 档，1300–4000 rpm
- 自动识别 PD 20V / QC / 5V 对应的最高转速
- 档位灯开关（不包含 RGB 灯光颜色与动画控制）
- 智能启停：关闭、立即运行、延时运行
- 底座通电自启动开关
- 断线自动重连，重连后自动恢复配置
- 设置保存在本机，重启后继续使用
- 原生 WinExe 后台入口，无 `dotnet.exe` 黑色控制台窗口
- 适配高 DPI / 200% Windows 缩放

默认配置为智能变频的性能曲线：

| 温度 | 30°C | 40°C | 50°C | 60°C | 70°C | 80°C | 90°C | 100°C |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 转速 | 1700 | 1700 | 2100 | 2700 | 3300 | 3500 | 4000 | 4000 |

## 构建

要求：Windows 10/11、.NET 8 SDK，以及已经在 Windows 蓝牙设置中完成配对的 BS2 Pro。

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

输出位于 `artifacts\publish`。项目通过 `Microsoft.Windows.SDK.NET` 使用 WinRT GATT，并通过 `LibreHardwareMonitorLib` 读取 CPU/GPU 温度。

当前设备蓝牙地址位于 `src/BS2Lite/Program.cs` 的 `DeviceAddress` 常量中。迁移到另一台底座时，需要将其改为 Windows 中对应设备的 BLE 地址后重新构建。

## 安装与管理

将 `artifacts\publish` 复制到一个固定目录，在该目录运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\管理.ps1 install
```

安装操作会请求一次管理员权限，用于登记登录时运行的计划任务；计划任务直接启动 `BS2Lite.exe`，不会显示控制台窗口。它还会创建桌面和开始菜单快捷方式，并关闭飞智空间站原有自启。

常用命令：

```powershell
.\管理.ps1 status
.\管理.ps1 show
.\管理.ps1 pause
.\管理.ps1 resume
.\管理.ps1 reconnect
.\管理.ps1 restart
.\管理.ps1 uninstall
```

正常使用时双击桌面的“BS2 Lite 控制面板”，或双击托盘图标。关闭控制面板只会隐藏到托盘，不会停止散热控制。

## 说明

本项目是针对个人硬件协议分析后制作的非官方工具，与飞智官方无关联。使用前请保留官方软件，以便固件升级或故障排查。
