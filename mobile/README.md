# Origami.Mobile — 行动端阅读客户端

基于 **Avalonia 12**（12.1.4）的多平台 Origami 阅读端：Windows Desktop、Android 与 iOS 共享同一套代码，只消费唯读的 `/api/v1` 接口（见根目录 README 第 7 节）。

## 功能（v1）

- 已发布文章列表：分页加载、置顶与密码保护标记、遮罩摘要
- 混合搜索：关键字（语意可用时标注"关键字＋语意"）
- 文章／独立页面详情：v1 以纯文本显示 Markdown 原文
- 设置页：API 服务器网址（持久化保存）

## 项目结构

```text
mobile/
  Origami.Mobile/            # 共享 net10.0：App、视图、ViewModel、API 客户端、设置
  Origami.Mobile.Desktop/    # Windows 桌面入口
  Origami.Mobile.Android/    # Android 入口（net10.0-android）
  Origami.Mobile.iOS/        # iOS 入口（net10.0-ios）
  Origami.Mobile.Tests/      # API 契约与客户端单元测试
  Origami.Mobile.slnx        # 独立 solution，不并入 Origami.slnx
```

独立 solution 的原因：服务器回归（`dotnet test tests/...`）不应依赖移动平台 workload。

## 运行（Windows Desktop）

```bash
# 1) 启动网站主机与 Api 主机（两个进程各绑一个端口）
ASPNETCORE_URLS=http://127.0.0.1:5000 dotnet run --project src/Origami.Web
ASPNETCORE_URLS=http://127.0.0.1:5100 dotnet run --project src/Origami.Api

# 2) 启动桌面客户端
dotnet run --project mobile/Origami.Mobile.Desktop
```

首次启动在 App 内「设置」确认 API 网址（默认 `http://localhost:5100`）。

## Android

- 需要 `android` workload（本机已装）、**Android SDK** 与 **JDK 21**。
  - Android SDK：本机已装于 `D:\Android\Sdk`；用 cmdline-tools 的 `sdkmanager` 安装，
    SDK 在自定义路径时以 `-p:AndroidSdkDirectory=<路径>` 传入。
  - JDK：.NET Android 工具链只接受 JDK 21（`Microsoft.OpenJDK.21`）；JDK 26 会报 XA0030，
    以 `-p:JavaSdkDirectory=<路径>` 指定 JDK 21 的安装目录。
- 本机已验证的构建命令：

```bash
dotnet build mobile/Origami.Mobile.Android/Origami.Mobile.Android.csproj -c Release \
  -p:AndroidSdkDirectory="D:/Android/Sdk" \
  -p:JavaSdkDirectory="C:/Program Files/Microsoft/jdk-21.0.13.21-hotspot"
```

  输出 APK 于 `mobile/Origami.Mobile.Android/bin/Release/net10.0-android/`。
  JDK 21 的版本字串（`21.0.13.0.2`）无法被工具链的版本解析器识别，会产生 XA0033 警告；
  构建结果不受影响。
- 模拟器访问开发机请把 App 设置改为 `http://10.0.2.2:5100`（10.0.2.2 即宿主机 localhost）。
- 清单已开启 `usesCleartextTraffic` 仅供开发连 `http://`；正式环境 API 必须使用 HTTPS。

## iOS

编译、真机签名、模拟器与 App Store 上架都需要配对的 macOS 机器；本仓库无法在 Windows 上完成 iOS 验证。

## 测试

```bash
dotnet test mobile/Origami.Mobile.Tests -c Release
```

测试使用脚本化的假 HTTP 传输，不连接真实服务器。

## 已知限制（v1）

- 正文为 Markdown 纯文本显示；富文本渲染、图片加载、代码高亮属于后续轮次。
- 无离线缓存、无推送通知、无身份验证（API 为唯读匿名接口）。
- 受密码保护与私密内容不可见（与 API 行为一致）；列表中受保护文章仅显示遮罩摘要。
