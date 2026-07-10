# BS2 Lite

<p align="center">
  <img src="assets/BS2Lite.png" width="128" alt="BS2 Lite icon">
</p>

面向飞智 BS2 Pro 压风式散热底座的轻量 Windows 后台控制器。发布版本是约 30 MB 的便携文件夹，不启动飞智空间站，也不会显示黑色控制台窗口。

## 功能

- 智能变频：静音、性能、自定义 8 点温控曲线
- **CPU 温度优先控制**；CPU 传感器异常时才回退到 GPU
- 固定转速：12 档，1300–4000 rpm
- 自动识别 PD 20V / QC / 5V 对应的最高转速
- 档位灯、智能启停、底座通电自启动
- 断线自动重连，重连后恢复上次配置
- 登录 Windows 后以最高权限静默在托盘运行
- 适配高 DPI / 200% Windows 缩放

RGB 灯光颜色、亮度和动画不包含在 Lite 中；连接时 RGB 保持关闭。

## 轻量化

- 智能模式每 2 秒读取温度，固定模式每 5 秒读取
- 电源类型每 60 秒复核一次
- 稳定运行时不写日志或配置文件
- 状态只在连接、温度、模式、转速等显示内容变化时写入约 1 KB
- 正常 ACK 和周期遥测不分配长期对象、不写日志
- 每次重连完整释放 GATT 服务、通知、队列和事件句柄
- 关闭控制面板会销毁 UI 控件，后台仅保留托盘与控制器
- 使用工作站并发 GC，不进行可能造成卡顿的周期性强制 GC

本机迁移 PawnIO 后 30 秒运行实测：磁盘写入 921 字节、私有内存约 76 MB、CPU 使用 0.484 CPU 秒（平均约占一个逻辑核心 1.61%）。

## CPU 温度驱动

BS2 Lite 1.3.0 起使用 `LibreHardwareMonitorLib 0.9.6` 与 [PawnIO 2.2.0](https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0) 读取 CPU 温度，不再生成或加载 WinRing0 / `BS2Lite.sys`。

PawnIO 是安装在 Windows 中的共享硬件访问驱动，不包含在便携文件夹里。请只安装 PawnIO 项目提供的官方签名版本，不要选择 `unrestricted` 版本，也不要为驱动关闭 Defender、内存完整性或添加安全排除项。

## 使用便携文件夹

1. 保持文件夹内的 EXE 和 DLL 在一起，将整个 `BS2 Lite` 文件夹放到准备长期使用的位置。
2. 安装 PawnIO 2.2.0 官方签名版；同一台电脑只需安装一次。
3. 双击 `BS2Lite.exe`，首次运行会请求一次 UAC，用于创建最高权限登录任务。
4. 设置完成后点击“保存并应用”，以后可直接关闭面板，程序继续在托盘运行。

移动时先从托盘菜单退出 BS2 Lite，再移动整个文件夹，然后从新位置双击一次 `BS2Lite.exe` 并确认 UAC。程序会自动更新计划任务路径，配置不会丢失。

配置、状态和日志保存在 `%LOCALAPPDATA%\FlydigiBS2Lite`，因此移动或替换程序文件夹不会影响散热设置。

默认性能曲线：

| 温度 | 30°C | 40°C | 50°C | 60°C | 70°C | 80°C | 90°C | 100°C |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 转速 | 1700 | 1700 | 2100 | 2700 | 3300 | 3500 | 4000 | 4000 |

## 构建

要求：Windows 10/11、.NET 8 SDK；运行电脑需要 .NET 8 Desktop Runtime 和 PawnIO 2.2.0 官方签名版。

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

构建结果位于 `artifacts\publish`。项目通过 `Microsoft.Windows.SDK.NET` 使用 WinRT GATT，并使用 `LibreHardwareMonitorLib 0.9.6` + PawnIO 读取 i9‑14900HX 等 CPU 的硬件温度。

当前设备蓝牙地址位于 `src/BS2Lite/Program.cs` 的 `DeviceAddress` 常量中；迁移到另一台底座时需要修改该地址并重新构建。

## 说明

本项目是针对个人硬件协议分析后制作的非官方工具，与飞智官方无关联。请保留官方软件，以便固件升级或故障排查。
