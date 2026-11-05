# GitHub 模板与功能来源

GitHub 星数由 GitHub API 于 **2026-10-12** 核实；星数是仓库星数，不代表单个模板的使用次数。岗位分类和填写引导由拾简增加，原仓库主要提供通用技术简历模板。

| 原仓库 | 核实星数 | 许可 | 固定提交 | 移植范围 |
| --- | ---: | --- | --- | --- |
| [posquit0/Awesome-CV](https://github.com/posquit0/Awesome-CV) | 28694 | LPPL-1.3c | `6701180c71479588dae5d895c4a10a6a572a40a0` | `awesome-cv.cls` 的抬头、强调色分节和经历条目；后端、嵌入式分类 |
| [billryan/resume](https://github.com/billryan/resume) | 11477 | MIT | `eadc3955cf006254dc8f2419e971fbe3f5bab413` | `resume.cls` / `resume.tex` 的居中抬头、单栏横线分节、日期条目；前端、后端、嵌入式分类 |
| [deedy/Deedy-Resume](https://github.com/deedy/Deedy-Resume) | 5066 | Apache-2.0 | `6d06ba7d8eee5fb8870bb00cb13a777f2083aa18` | `OpenFonts/deedy-resume-openfont.cls` / `.xtx` 的非对称双栏；前端、通用分类 |
| [jakegut/resume](https://github.com/jakegut/resume) | 2828 | MIT | `3e64a933141a7cda3a328f0743f73a201628a4ba` | `resume.tex` 的黑白单栏、居中抬头、紧凑条目；通用分类 |
| [ljy87263621/resume-helper](https://github.com/ljy87263621/resume-helper) | 2 | MIT | `6b1b69cf3bd2071c6f3cc17aeea41b492108ac80` | 模块状态、规则建议、A4 容量检测、AI JSON 提示词流程；非高星模板来源 |

`ThirdParty/` 保留上述原始模板文件、功能参考源码与完整许可证。对应在线原始文件可按表中固定提交访问。用于桌面预览和 PDF 的代码在 `client-winui/Services/ResumeRenderer.cs`，为拾简维护的 HTML/CSS 移植版本；原始 LaTeX 文件保持原名和原内容，移植代码不冒充上游维护版本。

适配变化：统一中文 A4、默认使用随包 MiSans，也可选择本机系统字体，取消对本机 LaTeX 和上游字体包的运行依赖；支持本应用模块编辑、照片、颜色和行距；示例个人信息替换为明确的占位内容；Deedy 依其原有结构将教育、技能、作品与荣誉置于左栏，项目和工作经历置于右栏。不能保证与上游 LaTeX 字体或分页逐像素一致。

MIT 来源的版权与许可见各目录 `LICENSE`；Deedy 为 Apache-2.0，原作者 Debarghya Das 的署名与许可见源文件及 `LICENSE`；Awesome-CV 原作者 Claud D. Park，许可见 `LICENCE`。对这些模板的 HTML/CSS 移植由 Resume Studio 维护，2026-10-12 起。

根目录 MIT 许可证仅适用于拾简原创代码。`ResumeRenderer.cs` 中 `.awesome` 版式适配段与对应预览保留 LPPL-1.3c，`.deedy` 版式及双栏结构适配段与对应预览保留 Apache-2.0，`.billryan` / `.jake` 段与对应预览保留各上游 MIT 署名。适配代码和预览采用拾简自己的文件名和模板标识；适配维护者为 `scj040921`，源代码在公开仓库内一同提供。运行组件许可另外见 [RUNTIME_NOTICES.md](RUNTIME_NOTICES.md)。

resume-helper 的功能规则在 `ResumeAdvisor.java` 中适配本应用模型；UI、AI 提示词和容量检测由 WinUI 客户端实现。复制提示词仅写入剪贴板；在线 AI 由用户配置兼容接口，主动请求时才发送所选内容。

## 职位来源

腾讯招聘使用官网公开查询接口；Arbeitnow 为可选公共职位源，异常明确显示。BOSS 等平台的职位收藏由用户复制链接或文本导入，不读取平台账号收藏。BOSS 的 [robots.txt](https://www.zhipin.com/robots.txt) 当前限制搜索参数路径，搜索页的普通 HTML 主要为脚本页面，当前版本不实现其匿名批量采集、验证码绕过或登录 Cookie 抽取。

v0.3.0 企业招聘系统接入依据官方公开资料：[Greenhouse Job Board API](https://docs.greenhouse.io/job-board.html)、[Lever Postings API](https://github.com/lever/postings-api)、[Ashby Public Job Postings](https://developers.ashbyhq.com/docs/public-job-posting-api)、[SmartRecruiters Posting API](https://developers.smartrecruiters.com/docs/posting-api)。Workday 接入验证于 NVIDIA 的公开职位板，只请求其公开岗位列表，不登录账号或提交申请。

## MiSans 字体

本软件使用由小米提供的 MiSans，原字体文件保持不变，随应用用于界面、简历预览和 PDF。字体许可独立于项目 MIT 许可证。官方来源：https://hyperos.mi.com/font/zh/download/ 。完整许可与字体来源见 Assets/Fonts/MiSans-License.pdf 和 Assets/Fonts/README.md。

原始 MiSans-Regular.ttf 的 SHA-256：9c120f0a849bc0aa5048daae2a3c0f6eecd828b5b33fce682a9622833f5feea6。

## PDF 题目导入

面试题 PDF 的文字与版面读取使用 [PdfPig 0.1.16](https://github.com/UglyToad/PdfPig)，由 UglyToad 与贡献者维护，采用 Apache-2.0。完整许可随应用保存在 `ThirdParty/PdfPig/LICENSE.txt`；本应用不执行 PDF 脚本，也不保留用户导入的原文档。

## Windows 安装器

Setup 使用 Inno Setup 6.7.3 构建，来源为 [JRSoftware 官方发行版](https://github.com/jrsoftware/issrc/releases/tag/is-6_7_3)。安装器安装 Inno Setup 的完整许可至 licenses/InnoSetup-LICENSE.txt。构建工具的原始 SHA-256 为 9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732，并验证 Pyrsys B.V. 的 Authenticode 签名。

简体中文安装器文本保留 [Inno Setup 官方仓库的 ChineseSimplified.isl](https://github.com/jrsoftware/issrc/blob/main/Files/Languages/ChineseSimplified.isl) 原内容与译者署名，固定随源码分发。文件 SHA-256 为 e0b0b350e2245f3c5e65586dfe43d574f6e7f06f2261149aba284954b3fc9a8d。

安装包包含微软官方 WebView2 Evergreen Bootstrapper，仅在所需运行时缺失时安装。来源与分发方式见 [Microsoft WebView2 分发说明](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)，构建时验证 Microsoft Corporation 发布者签名。WebView2 的许可独立于本项目 MIT 许可证。
# Android PDFBox

Android document import uses [PdfBox-Android](https://github.com/TomRoush/PdfBox-Android) 2.0.27.0, an Android port of Apache PDFBox / FontBox, under Apache License 2.0. Copyright notices and license resources from the original AAR/JAR are retained in the APK. The dependency is downloaded from Maven Central with a pinned SHA-256 during Android build; it is not part of the Windows runtime.

## Android PDF 简历 OCR

Google ML Kit Text Recognition v2 Chinese 16.0.1 及固定版本支持组件用于本机扫描件识别。SDK 条款、数据说明与依赖归属见 `ThirdParty/AndroidOCR/NOTICE.md`；精确版本和 SHA-256 见 `scripts/android-ocr-dependencies.json`。

## Android WebView 交互组件与动效

Android WebView 将选择性打包 Microsoft Fluent UI Web Components 3.1.4（MIT；按钮组件）与 GSAP 3.15.1（GSAP Standard License；免费用于商业项目，许可见 https://gsap.com/standard-license/）。精简 Bundle 由 Node.js/npm 与 esbuild 生成，仅内置使用的 Fluent 按钮组件和 GSAP 核心；APK 运行时不访问 CDN。依赖精确版本见 `client-android/package-lock.json`。
