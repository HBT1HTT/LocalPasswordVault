# 本地密码库（LocalPasswordVault）

一个只在 Windows 本机运行的密码管理小工具：启动时必须通过 **Windows Hello** 系统验证，账号、密码、附加信息使用 **DPAPI（CurrentUser）** 加密后存入本地 **SQLite**，列表只显示条目名称明文。

## 功能

- 启动解锁页：检测 Windows Hello 是否可用，并唤起系统验证窗口
- 主界面：条目名称列表、详情区、密码默认遮挡并可显示/隐藏
- 新建 / 编辑条目：名称（必填）、账号、密码、附加信息（多行）
- 删除条目（可在设置中要求每次删除都验证 Windows Hello）
- 闲置自动锁定（默认 5 分钟，可在设置中调整或关闭）
- 单实例运行：重复启动只会激活已有窗口，不会打开第二个密码库
- 主密码加密导出 / 导入（`.vault`，可跨电脑恢复；导入前自动备份 `.bak`）
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
LocalPasswordVault.csproj     项目配置（自包含单文件、版本、许可证标识）
LICENSE                       GPL-3.0 官方许可证全文
App.xaml / App.xaml.cs        应用入口、全局样式与单实例处理
MainWindow.xaml               主窗口（承载三个视图、验证遮挡层）
Models/                       条目数据模型
Services/
  HelloAuthService.cs         Windows Hello 可用性检测与验证
  EntryStore.cs               SQLite 存储服务（统一加解密入口）
  DpapiProtector.cs           DPAPI 加解密封装
  SingleInstanceGuard.cs      命名 Mutex + 命名管道单实例保护
  VaultBackupCrypto.cs        .vault 备份的 PBKDF2 + AES-256-GCM 封装
  VaultBackupService.cs       加密备份导出 / 导入与回滚编排
  StorageExceptions.cs        存储/解密异常类型
  Log.cs                      非敏感运行日志
Views/
  UnlockView                  解锁页
  MainView                    主界面（列表/详情/空状态）
  EntryEditView               新建/编辑表单
  SettingsWindow              设置（删除保护、闲置锁定、数据备份）
  ExportBackupWindow          导出：设置主密码
  ImportBackupWindow          导入：选择文件与主密码
tools/GenerateIcon.ps1        生成多尺寸 app.ico
```

## 已知限制

- 仅在开发者本机完成构建、发布与冒烟验证；真实 Windows Hello 交互、修改 PIN 后再次验证、在未安装 .NET 的机器上运行、跨电脑导入等，需要人工验收
- 单实例保护只防止本机同一用户会话重复打开密码库，不能防御内存读取、进程注入、录屏或管理员级工具
- .NET 中字符串无法可靠擦除；程序只对密钥、JSON 载荷等 `byte[]` 调用 `CryptographicOperations.ZeroMemory`，无法保证托管字符串立即从内存消失
- 本机 `passwords.db` 仍由 DPAPI（CurrentUser）保护，换 Windows 用户或换电脑后需通过 `.vault` 备份恢复
- 导入会替换整个密码库，覆盖前自动在同目录生成 `passwords.db.<时间戳>.bak`
- 旧版 `HBTV` / `.hbtvault` 备份不再支持导入，会提示用新版重新导出

## 许可证

本项目采用 **GNU General Public License v3.0（GPL-3.0）** 发布，完整条款见仓库根目录的 [LICENSE](LICENSE)，官方文本：<https://www.gnu.org/licenses/gpl-3.0.html>。

LocalPasswordVault 是本地密码管理工具，处理的是账号与密码等敏感数据。GPL-3.0 是 copyleft 许可证：你可以自由使用、研究、修改和分发本软件，但分发修改版或衍生作品时，必须同样以 GPL-3.0 开源并提供对应源代码，不得附加额外限制。本项目不提供 MIT 或双许可选项。
