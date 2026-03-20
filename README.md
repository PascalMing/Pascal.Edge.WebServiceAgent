# Pascal.Edge.WebServiceAgent

Vue多站点静态文件托管服务，基于ASP.NET Core实现，支持Host头路由、配置文件热加载、SPA路由回退和HTTP代理转发。

## 🎯 核心功能

- **多站点托管**：每个站点对应独立端口和目录，支持10+个Vue项目同时托管
- **Host头路由**：基于HTTP Host头智能路由，支持多个域名绑定到同一端口
- **SPA路由支持**：统一启用history模式的Vue Router路由回退
- **HTTP代理转发**：支持将请求转发到其他IP+端口，取代nginx反向代理
- **配置热加载**：支持运行时修改`appsettings.json`，自动生效无需重启
- **默认站点**：Host头不匹配时自动回退到默认站点

## 📁 部署目录结构

```
Pascal.Edge.WebServiceAgent/
├── appsettings.json
├── Program.cs
├── Models/
│   └── SiteOptions.cs      # 站点配置模型
├── Services/
│   └── SiteConfigurationLoader.cs  # 配置加载与热加载
└── www-dist/
    ├── home/               # welcome.good.cn → 8050
    │   └── index.html
    └── game/               # game.good.cn → 8051
        └── index.html
```

## ⚙️ 配置文件

### appsettings.json 结构

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Error",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "DefaultDocument": "index.html",
  "EnableSPAFallback": true,
  "DefaultSite": "home",
  "Sites": [
    {
      "Name": "home",
      "Port": 8050,
      "Hostnames": ["welcome.good.cn"],
      "Path": "./www-dist/home"
    },
    {
      "Name": "game",
      "Port": 8051,
      "Hostnames": ["game.good.cn"],
      "Path": "./www-dist/game"
    },
    {
      "Name": "api",
      "Port": 8053,
      "Hostnames": ["api.good.cn"],
      "ForwardUrl": "http://www.pascaledge.cn:5000"
    }
  ]
}
```

### 配置说明

| 配置项 | 类型 | 必填 | 说明 |
|--------|------|------|------|
| `DefaultDocument` | string | 否 | 默认文档名，默认`index.html` |
| `EnableSPAFallback` | bool | 否 | 是否启用SPA路由回退，默认`true` |
| `DefaultSite` | string | 否 | 默认站点名称，当Host不匹配时使用 |
| `Sites` | array | 是 | 站点配置数组 |
| `Sites[].Name` | string | 是 | 站点名称（唯一标识） |
| `Sites[].Port` | int | 是 | 监听端口 |
| `Sites[].Hostnames` | string[] | 是 | 绑定的域名列表 |
| `Sites[].Path` | string | 条件 | 静态文件目录路径（与`ForwardUrl`二选一） |
| `Sites[].ForwardUrl` | string | 条件 | 代理转发目标URL（与`Path`二选一） |

## 🚀 快速开始

### 1. 部署Vue项目

将Vue项目的`dist`目录内容复制到`www-dist/{站点名}/`目录：

```bash
# 示例：部署首页和游戏站点
cp -r /path/to/vue-home/dist/* ./www-dist/home/
cp -r /path/to/vue-game/dist/* ./www-dist/game/
```

### 2. 配置DNS/Hosts

在DNS服务器或本地`hosts`文件中添加域名解析：

```bash
# Windows: C:\Windows\System32\drivers\etc\hosts
# Linux/Mac: /etc/hosts

127.0.0.1 welcome.good.cn
127.0.0.1 game.good.cn
127.0.0.1 api.good.cn
```

### 3. 启动服务

```bash
dotnet run
```

服务将在配置的端口上启动：
- 首页：`http://welcome.good.cn:8050`
- 游戏：`http://game.good.cn:8051`
- API代理：`http://api.good.cn:8053` → 转发到 `http://www.pascaledge.cn:5000`

## 🔧 添加新站点

### 静态文件站点

```json
{
  "Sites": [
    {
      "Name": "admin",
      "Port": 8054,
      "Hostnames": ["admin.good.cn"],
      "Path": "./www-dist/admin"
    }
  ]
}
```

### 代理转发站点

```json
{
  "Sites": [
    {
      "Name": "backend",
      "Port": 8055,
      "Hostnames": ["backend.good.cn"],
      "ForwardUrl": "http://192.168.1.101:8080"
    }
  ]
}
```

**配置文件变更会自动检测并热加载**，无需重启服务。

## 📝 架构说明

### 请求处理流程

```
Client Request
    ↓
Kestrel (多端口监听)
    ↓
根据Host头匹配站点配置
    │
    ├─ 匹配成功 → 使用该站点配置
    │
    └─ 匹配失败 → 按请求端口选择站点
                    │
                    ├─ 端口匹配成功 → 使用该端口站点配置
                    │
                    └─ 端口也失败 → 使用默认站点配置
    ↓
┌─────────────────────────────────────┐
│  根据站点配置选择处理方式            │
├─────────────────┬───────────────────┤
│  Path 模式      │  ForwardUrl 模式  │
│  ↓              │  ↓                │
│  读取本地静态文件 │  HTTP代理转发      │
│  ↓              │  ↓                │
│  SPA回退        │  返回远程响应       │
└─────────────────┴───────────────────┘
    ↓
Response
```

### 站点匹配优先级

1. **域名匹配**：根据请求的Host头精确匹配站点配置中的Hostnames
2. **端口匹配**：域名不匹配时，使用请求端口匹配对应站点
3. **默认站点**：域名和端口都不匹配时，使用DefaultSite配置的站点

### 两种站点模式

| 模式 | 用途 | 示例场景 |
|------|------|----------|
| `Path` | 静态文件托管 | Vue/React项目、前端页面 |
| `ForwardUrl` | HTTP代理转发 | 后端API、旧系统、Nginx替代 |

## 🛠️ 技术栈

- **.NET 10.0**
- **ASP.NET Core**
- **Kestrel** (跨平台Web服务器)

## 📄 许可证

MIT License
