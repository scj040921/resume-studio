# 拾简 Resume Studio

<img src="client-winui/Assets/AppLogo.png" width="80" alt="拾简图标">

Windows / Android 简历与求职管理软件。电脑使用 WinUI 3 与本地 Java 服务，安卓离线存储，两端可通过 WebDAV 同步。[安卓安装与同步说明](docs/ANDROID.md)

## 下载与安装

从 [GitHub Releases](https://github.com/scj040921/resume-studio/releases) 下载 `Setup-x64.exe`，点击“一键安装”，自动创建桌面和开始菜单快捷方式。也可使用完整的便携 ZIP。

需要 Windows 10 2004 或更新的 x64 系统。.NET 与 Java 随包提供；安装器会在缺少 WebView2 时联网补装。覆盖安装和卸载保留个人数据。

## 主要功能

- **简历工作台**：多份简历、模块编辑与拖动排序、顶栏排版设置、MiSans 与字体选择、照片、格式按钮、自动保存、撤销和历史版本。
- **模板与预览**：8 套开源岗位适配模板及 6 套兼容模板；有照片／无照片双预览。未填写处显示示例，填写后逐模块替换；PDF 只导出实际内容。
- **岗位优化**：本地职位对照；可配置兼容 AI 接口，逐项确认建议后生成独立岗位简历。
- **求职管理**：企业职位查询、链接／分享文本收藏、收藏摘要与点击展开、分页筛选、投递时间线、简历快照与复盘；投递和笔试／面试／截止日程相互关联与定位，重复收藏保护备注。
- **面试题背诵**：导入 Markdown、TXT、DOCX 和文字型 PDF，按小标题拆成题目卡片；导入前可选择标题层级并预览。单题阅读、隐藏答案、滚轮／点击／方向键切题，分别统计已记住与未掌握。题库和掌握进度可在新版电脑与安卓间手动同步，不复制原文档；扫描 PDF 需先做 OCR。
- **会话采集与导航**：在 BOSS／牛客打开具体职位详情，点击“收藏当前职位”仅保存这一条，读取职责、任职要求、公司、地点与薪资。列表、推荐职位和浏览翻页不会自动收藏；正文未加载时提示重试。已有收藏只补全空缺，不覆盖手写标题、正文或备注。登录和验证需本人完成。
- **手动同步**：设置页可配置 WebDAV，整包同步简历、照片、历史、收藏、投递、日程、公司来源和非敏感设置。首次同步自动创建应用文件夹；支持强 ETag 时使用条件写入，缺少强 ETag 时使用内容校验，请同一时间只在一台设备同步。两端一致时无需同步；有差异就比较大小变化和文件修改时间，确认保留本机并上传或保留云端并下载。下载替换前备份本机资料。[同步协议](docs/SYNC-PROTOCOL.md)说明格式和恢复方式。
- **界面与更新**：经典／Haru 界面、主题与动效设置，Stable／Nightly 更新和公益代理备用通道。
- **轻量发行**：保留自包含运行环境、完整字体及模板；部分裁剪未使用框架代码，维护超额 HTTP 磁盘缓存并保留登录状态。

数据保存在 `%LOCALAPPDATA%\ResumeStudio`。WebDAV 同步包在用户选择的服务器上以明文保存；Cookie、API Key 和同步密码仅留本机。当前没有 Word 导出；职位覆盖和访问状态以官网为准，不同步平台账号收藏。AI 仅在主动请求时发送内容，Key 按当前 Windows 用户加密保存。日程提醒仅在软件运行时检查。真实平台登录、线上 WebDAV 和干净 Windows 分发仍需验收，详见[集成说明](docs/TAKEOVER.md)。

## WebDAV 配置

填写服务提供的入口或已有文件夹地址，新配置默认在其下创建并使用 `ResumeStudio/`。坚果云可直接填 `https://dav.jianguoyun.com/dav/`，用户名填坚果云账号，密码填“安全选项 → 第三方应用管理”生成的应用密码。已有实际同步目录保持不变，已填 `ResumeStudio/` 时不会重复追加；旧坚果云 `/dav/` 入口会转为专用目录，目录变化时需重新填写应用密码。服务入口应已存在，程序不递归创建父目录。

服务缺少强 ETag 时，程序用完整文件的 SHA-256 指纹检查变化、上传前复查、上传后回读验证。这无法完全阻止最后一次检查与上传之间的并发覆盖，因此请避免多个设备同时同步。

## 开发

需要 Windows、PowerShell 7、.NET 8 SDK 和 JDK 21，设置 `JAVA_HOME` 后运行：

```powershell
./scripts/build.ps1
./scripts/test-offline.ps1
./scripts/test-workbench.ps1
./scripts/test-features.ps1 -ReuseBackendTestClasses
```

完整程序位于 `release/ResumeStudio/`。构建产物、验证输出、个人数据和私钥不提交到仓库；生成的模板目录由构建脚本重建。

## 文档

- [安装与卸载](docs/INSTALLATION.md) · [更新与发布](docs/UPDATES.md)
- [开发、结构与验证](docs/DEVELOPMENT.md) · [版本记录](docs/CHANGELOG.md) · [图标](docs/BRAND.md)
- [模板与字体许可](THIRD_PARTY_NOTICES.md) · [运行组件许可](RUNTIME_NOTICES.md)

原创代码采用 [MIT](LICENSE)，第三方模板、字体和运行组件遵循各自许可。
