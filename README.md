# DeskPin

DeskPin 是一个面向 Windows 10/11 x64 的窗口置顶工具。它提供桌面窗口列表、搜索、置顶切换、系统托盘、可配置全局快捷键和可选开机启动。

## 开发运行

要求安装 .NET 10 SDK。普通调试构建不需要额外工具：

```powershell
dotnet build .\src\DeskPin\DeskPin.csproj
dotnet run --project .\src\DeskPin\DeskPin.csproj
```

启动参数 `--background` 会跳过主界面并直接驻留托盘。

## 测试

```powershell
dotnet test .\tests\DeskPin.Tests\DeskPin.Tests.csproj
```

测试包含窗口过滤、搜索、快捷键、设置持久化，以及在交互式 Windows 桌面中运行的真实窗口置顶集成测试。

## 构建 MSI

```powershell
.\build.cmd
```

WiX 5.0.2 通过项目 SDK 自动还原，不要求全局安装，也不要求接受 WiX 7 的 OSMF EULA。构建会发布自包含的 `win-x64` 单文件应用，并生成当前用户范围的中文 MSI。安装向导默认安装到 `%LocalAppData%\Programs\DeskPin`，也可以选择其他当前用户可写的本地目录。默认输出：

```text
installer\DeskPin.Installer\bin\x64\Release\DeskPin-x64.msi
```

正式发布启用自包含单文件压缩，仅携带简体中文卫星资源，并使用原生 Win32 托盘实现以避免引入 WinForms 运行时。构建脚本会显示 EXE/MSI 的实际字节数，并在 EXE 超过 60 MiB 或 MSI 超过 55 MiB 时失败。使用 .NET SDK 10.0.301 的当前验证结果为：EXE 58.79 MiB，MSI 52.78 MiB。

MSI 和应用未进行代码签名，公开分发前应使用可信证书签名。

仓库也提供等价的 `build.ps1`；如果系统禁止运行本地 PowerShell 脚本，请直接使用上面的 `build.cmd`，无需修改执行策略。

## 行为边界

- 默认没有全局快捷键，也不会开机启动；两项均可在设置中启用。
- 点击主窗口关闭按钮只会隐藏到系统托盘。
- 从托盘退出时，DeskPin 会取消本次运行期间由它新增的置顶状态。
- 普通权限进程可能无法控制以管理员身份运行的窗口，DeskPin 会显示明确提示但不会自动提权。
- 不保存自动置顶规则，窗口或 DeskPin 重启后不会自动恢复置顶状态。
