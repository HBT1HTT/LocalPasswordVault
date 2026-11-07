# 本地密码库（LocalPasswordVault）

一个只在 Windows 本机运行的密码管理小工具：启动时必须通过 **Windows Hello** 系统验证，账号、密码、附加信息使用 **DPAPI（CurrentUser）** 加密后存入本地 **SQLite**，列表只显示条目名称明文。

## 功能

- 启动解锁页：检测 Windows Hello 是否可用，并唤起系统验证窗口
- 主界面：条目名称列表、详情区、密码默认遮挡并可显示/隐藏
- 新建 / 编辑条目：名称（必填）、账号、密码、附加信息（多行）
- 空状态、解密失败、数据库异常均有明确提示
- 无网络依赖，不需要账号，不读写 exe 同目录文件

## 安全模型

- 不读取、不保存、不模拟 Windows 登录密码或 PIN，也不内置任何写死的密码
- 只调用 `Windows.Security.Credentials.UI.UserConsentVerifier.RequestVerificationAsync` 获取“通过/不通过”结果，由 Windows 自己完成验证
- 验证失败、取消、Windows Hello 不可用都不会进入主界面，也不会加载任何密码数据
- 数据库中的账号、密码、附加信息只保存 DPAPI 密文（`DataProtectionScope.CurrentUser`）
- 列表页只查询名称；查看详情时才解密，切换条目或离开详情后不残留明文
- 日志只记录非敏感运行信息，不记录账号、密码、附加信息
- 程序重启必须重新验证，不跨进程记住“已解锁”状态

> 数据与当前 Windows 用户绑定：把 `passwords.db` 复制到其他 Windows 用户或其他电脑后无法解密，这是 DPAPI 的预期行为。

## 直接使用（无需 .NET）

从 Releases 页面下载 `LocalPasswordVault.exe`，双击运行即可。该文件是 win-x64 自包含单文件，目标机器不需要安装 .NET 运行时。

系统要求：

- Windows 10 1809 或更高版本
- 已在“设置 → 账户 → 登录选项”中配置 PIN / 指纹 / 人脸（Windows Hello）

数据位置：

- 数据库：`%LocalAppData%\LocalPasswordVault\passwords.db`（首次运行自动建表）
- 日志：`%LocalAppData%\LocalPasswordVault\app.log`

## 从源码构建

需要 Windows 与 .NET SDK 10。

```powershell
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None -p:DebugSymbols=false -o publish
```

产物为 `publish\LocalPasswordVault.exe` 单文件。

说明：

- 图标文件为 `Assets/app.ico`；如需调整图标，可修改 `tools/GenerateIcon.ps1` 中的颜色/形状后重新生成（会输出多尺寸 ico 与预览图）

## 项目结构

```
LocalPasswordVault.csproj     项目配置（自包含单文件、应用图标）
App.xaml / App.xaml.cs        应用入口与全局样式
MainWindow.xaml               主窗口（承载三个视图，设置窗口图标）
Models/                       条目数据模型
Services/
  HelloAuthService.cs         Windows Hello 可用性检测与验证
  EntryStore.cs               SQLite 存储服务（统一加解密入口）
  DpapiProtector.cs           DPAPI 加解密封装
  StorageExceptions.cs        存储/解密异常类型
  Log.cs                      非敏感运行日志
Views/
  UnlockView                  解锁页
  MainView                    主界面（列表/详情/空状态）
  EntryEditView               新建/编辑表单
tools/GenerateIcon.ps1        生成多尺寸 app.ico
```

## 已知限制

- 仅在开发者本机完成构建、发布与冒烟验证；真实 Windows Hello 交互、修改 PIN 后再次验证、在未安装 .NET 的机器上运行等，需要人工验收
- 条目暂不支持删除、搜索、导入/导出等功能

## 许可证

本仓库暂未附带开源许可证。如需转载或分发，请先联系作者。
