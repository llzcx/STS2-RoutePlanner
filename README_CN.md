<a id="readme-top"></a>

<!-- LANGUAGE SWITCH -->
<div align="center">

[English](README.md) | 简体中文

</div>

<!-- PROJECT POSTER -->
<div align="center">
  <img src="doc/mod.png" alt="路线导航仪截图" width="80%">
</div>

---

<!-- PROJECT LOGO -->
<br />
<div align="center">

<h3 align="center">🗺️ 路线导航仪</h3>

  <p align="center">
    杀戮尖塔 2 智能路线规划模组 —— 让数据引导你的每一次攀登。
    <br />
    <a href="https://github.com/llzcx/STS2-RoutePlanner"><strong>探索文档 »</strong></a>
    <br />
  </p>

  <!-- PROJECT SHIELDS -->
[![Contributors][contributors-shield]][contributors-url]
[![Forks][forks-shield]][forks-url]
[![Stargazers][stars-shield]][stars-url]
[![Issues][issues-shield]][issues-url]
[![License][license-shield]][license-url]

  <p align="center">
    <a href="https://github.com/llzcx/STS2-RoutePlanner/issues/new?labels=bug&template=bug-report---.md">报告 Bug</a>
    &middot;
    <a href="https://github.com/llzcx/STS2-RoutePlanner/issues/new?labels=enhancement&template=feature-request---.md">请求功能</a>
  </p>
</div>



<!-- TABLE OF CONTENTS -->
<details>
  <summary>目录</summary>
  <ol>
    <li>
      <a href="#项目简介">项目简介</a>
      <ul>
        <li><a href="#核心功能">核心功能</a></li>
        <li><a href="#技术栈">技术栈</a></li>
      </ul>
    </li>
    <li>
      <a href="#快速开始">快速开始</a>
      <ul>
        <li><a href="#前置条件">前置条件</a></li>
        <li><a href="#安装">安装</a></li>
      </ul>
    </li>
    <li><a href="#使用指南">使用指南</a></li>
    <li><a href="#评分模型">评分模型</a></li>
    <li><a href="#路线图">路线图</a></li>
    <li><a href="#贡献">贡献</a></li>
    <li><a href="#许可证">许可证</a></li>
    <li><a href="#联系方式">联系方式</a></li>
  </ol>
</details>



<!-- ABOUT THE PROJECT -->
## 📖 项目简介

路线导航仪用**双重维度动态规划引擎**取代了凭感觉选路的决策方式。它评估地图上每条可行路径，将危险度和奖励值量化为可比较的分数——并实时根据角色状态和遗物进行修正。

不再是"这条路线感觉还行"，让数据帮你走得更远。

### 核心功能

- 🧠 **DP 路径搜索** — 深度优先动态规划，毫秒级找到从起点到 Boss 的最优路线
- ⚖️ **双重维度评分** — 每个节点独立计算危险分和奖励分，通过可调权重合成总分
- 🩸 **自适应状态感知** — 低血量？缺防御？没药水？危险分数随角色状态实时调整
- 🏆 **遗物感知修正** — 弹弓、战锤、黑星等遗物自动调整精英节点评分
- 🎚️ **连续参数调节** — 双滑块（危险容限 / 收益渴求）提供无级调节，外加 5 个快捷预设
- 📊 **基础分数编辑器** — 完整 GUI 自定义各节点类型基础分值，公式链路透明可见
- 🔒 **节点约束系统** — 独立设置每种类型的 ≥/≤ 数量限制，精确控制路线组成
- 🎯 **优先级模式** — 按重要性排列节点类型，引擎寻找最符合优先级的最优路线
- 🎨 **原生地图绘制** — 直接调用游戏内置画笔 API 在地图上绘制路线，非叠加层 hack。联机模式下所有玩家互相可见。
- 🌐 **国际化支持** — 完整中英文切换，易于扩展
- 🔥 **配置热加载** — 游戏运行时编辑 JSON 配置，即时生效无需重启

<p align="right">(<a href="#readme-top">回到顶部</a>)</p>



### 技术栈

- [![C#][CSharp]][CSharp-url] .NET 9.0
- [![Godot][Godot]][Godot-url] 4.5.1 Mono
- Harmony — 运行时补丁

<p align="right">(<a href="#readme-top">回到顶部</a>)</p>



<!-- GETTING STARTED -->
## 🚀 快速开始

### 前置条件

- 杀戮尖塔 2（Godot 4.5.1 Mono 版本）
- .NET 9.0 运行时

### 安装

1. 从 [Releases](https://github.com/llzcx/STS2-RoutePlanner/releases) 下载最新 `RoutePlanner_v1.1.0.zip`
2. 解压到游戏 `mods/RoutePlanner/` 目录
3. 确保目录结构如下：
   ```
   mods/RoutePlanner/
   ├── manifest.json
   ├── route_planner.dll
   ├── config/
   │   ├── route_planner_scoring.json
   │   └── route_planner_settings.json
   └── locale/
       ├── en.json
       └── zh.json
   ```
4. 启动游戏，进入地图界面后导航仪面板自动出现

<p align="right">(<a href="#readme-top">回到顶部</a>)</p>



<!-- USAGE -->
## 💻 使用指南

### 快速上手

进入地图，面板自动显示并预先计算好路线。点击预设即可：

| 预设 | 含义 |
|------|------|
| **保守** | 纯避险，不追收益 |
| **求稳** | 回避危险，适度积累 |
| **均衡** | 危险与收益并重 |
| **激进** | 追逐危险，获取战利品 |
| **极端** | 全精英极限冲分 |

### 精细调节

1. 拖动**危险容限**和**收益渴求**滑块
2. 开启**自动绘制**——路线实时更新
3. 切换 4 种航线模式：**自定义**、**定向**（优先级）、**高收益**、**安全**

### 高级玩法

- 点击**齿轮图标**打开基础分数编辑器，自定义评分公式
- 在星图数据中设置**节点类型约束**（≥ 至少 / ≤ 最多）
- 用 ▲▼ 调整**节点优先级**引导定向模式
- 编辑 `config/route_planner_scoring.json` 深度定制——修改即时生效

<p align="right">(<a href="#readme-top">回到顶部</a>)</p>



<!-- SCORING MODEL -->
## 📐 评分模型

<div align="center">
  <img src="doc/setting.png" alt="基础分数编辑器" width="70%">
  <p><em>基础分数编辑器 — 调整各节点类型的危险/奖励值，实时预览公式链路</em></p>
</div>

```
最终危险分 = 基础危险值[类型] × 危险倍率(状态) × 精英遗物修正
最终奖励分 = 基础奖励值[类型] × 奖励倍率(状态) × 精英遗物修正

路径总分 = Σ ( 收益渴求 × 奖励分 + 危险系数 × 危险分 )
危险系数 = 2 × 危险容限 − 1   (范围: −1 至 +1)
```

| 状态 | 影响 |
|------|------|
| 低血量 (<30%) | 危险 ×1.30 |
| 防御卡比例低 | 危险 ×1.25 |
| 无药水 | 危险 ×1.15 |
| 金币不足 | 商店奖励 ×0.90 |
| 遗物数少 | 宝箱/事件奖励 ×1.10 |
| 满血 | 休息点奖励 ×0.90 |

| 遗物 | 修正 |
|------|------|
| 弹弓 / 轰鸣海螺 / 毛皮大衣 | 精英危险 −10% |
| 战锤 / 白星 / 黑星 | 精英奖励 +10% |
| 石中剑 | 精英奖励 +5%（击杀<4只精英） |

<p align="right">(<a href="#readme-top">回到顶部</a>)</p>



<!-- ROADMAP -->
## 🗺️ 路线图

- [ ] 路线风险分析——标注每条路线的最危险节点
- [ ] 历史路线回溯——记录并对比之前的选择
- [ ] 多路线平行对比（2–3 条）
- [ ] 社区共享评分预设
- [ ] 路线节点悬停详情预览

查看 [open issues](https://github.com/llzcx/STS2-RoutePlanner/issues) 获取完整功能规划。

<p align="right">(<a href="#readme-top">回到顶部</a>)</p>



<!-- CONTRIBUTING -->
## 🤝 贡献

开源社区因贡献而精彩。任何形式的贡献都**非常欢迎**。

1. Fork 本项目
2. 创建功能分支 (`git checkout -b feature/AmazingFeature`)
3. 提交变更 (`git commit -m 'feat: 添加某某功能'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 开启 Pull Request

<p align="right">(<a href="#readme-top">回到顶部</a>)</p>



<!-- LICENSE -->
## 🎗 许可证

Copyright © 2025 [Shiang Chen](https://github.com/llzcx).

基于 [MIT][license-url] 许可证发布。

<p align="right">(<a href="#readme-top">回到顶部</a>)</p>



<!-- CONTACT -->
## 📧 联系方式

Shiang Chen — [@llzcx](https://github.com/llzcx)

项目链接: [https://github.com/llzcx/STS2-RoutePlanner](https://github.com/llzcx/STS2-RoutePlanner)

<p align="right">(<a href="#readme-top">回到顶部</a>)</p>



<!-- STAR HISTORY -->
## ⭐ Star 历史

<div align="center">
  <a href="https://star-history.com/#llzcx/STS2-RoutePlanner&Date">
    <img src="https://api.star-history.com/svg?repos=llzcx/STS2-RoutePlanner&type=Date" alt="Star History Chart" width="800">
  </a>
</div>



<!-- REFERENCE LINKS -->
[contributors-shield]: https://img.shields.io/github/contributors/llzcx/STS2-RoutePlanner.svg?style=flat-round
[contributors-url]: https://github.com/llzcx/STS2-RoutePlanner/graphs/contributors
[forks-shield]: https://img.shields.io/github/forks/llzcx/STS2-RoutePlanner.svg?style=flat-round
[forks-url]: https://github.com/llzcx/STS2-RoutePlanner/network/members
[stars-shield]: https://img.shields.io/github/stars/llzcx/STS2-RoutePlanner.svg?style=flat-round
[stars-url]: https://github.com/llzcx/STS2-RoutePlanner/stargazers
[issues-shield]: https://img.shields.io/github/issues/llzcx/STS2-RoutePlanner.svg?style=flat-round
[issues-url]: https://github.com/llzcx/STS2-RoutePlanner/issues
[license-shield]: https://img.shields.io/github/license/llzcx/STS2-RoutePlanner.svg?style=flat-round
[license-url]: https://github.com/llzcx/STS2-RoutePlanner/blob/master/LICENSE
[CSharp]: https://img.shields.io/badge/C%23-512BD4?style=flat-round&logo=csharp&logoColor=white
[CSharp-url]: https://dotnet.microsoft.com/en-us/languages/csharp
[Godot]: https://img.shields.io/badge/Godot-478CBF?style=flat-round&logo=godotengine&logoColor=white
[Godot-url]: https://godotengine.org/
