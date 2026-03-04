# LanMountainDesktop Recommendation Backend

信息推荐后端，提供统一抓取与聚合接口，当前覆盖：
- 每日一言
- 每日诗词
- 每日电影推荐
- 每日名画
- 百度热搜

## 启动

```bash
dotnet run --project LanMountainDesktop.RecommendationBackend/LanMountainDesktop.RecommendationBackend.csproj
```

默认监听地址以 `dotnet` 输出为准（通常是 `http://localhost:5xxx` 或 `https://localhost:7xxx`）。

## 接口

- `GET /health`
- `GET /api/recommendation/daily-quote?locale=zh-CN&forceRefresh=false`
- `GET /api/recommendation/daily-poetry?locale=zh-CN&forceRefresh=false`
- `GET /api/recommendation/daily-movie?candidateCount=20&forceRefresh=false`
- `GET /api/recommendation/daily-artwork?candidateCount=50&forceRefresh=false`
- `GET /api/recommendation/hot-search?provider=Baidu&limit=10&forceRefresh=false`
- `GET /api/recommendation/feed?locale=zh-CN&hotSearchLimit=10&forceRefresh=false`
- `POST /api/recommendation/cache/clear`

## 设计说明

- 服务实现风格与现有天气服务一致：`Options + Query + QueryResult + Service`。
- 所有抓取接口都带有统一错误返回：`errorCode` + `errorMessage`。
- 提供内存缓存，降低上游请求频率与组件刷新开销。
