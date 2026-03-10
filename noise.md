# 噪音监测与评分说明

## 中文

本文档描述阑山桌面中噪音监测能力的基本设计目标、核心指标和实现边界。

### 设计目标

- 用尽量稳定的方式度量环境噪音。
- 区分持续噪音、超阈值时长和高频打断。
- 将结果反馈到实时界面、历史报表和专注场景中。

### 核心指标

- `p50Dbfs`：持续噪音水平，反映环境底噪。
- `overRatioDbfs`：超阈值时间占比，反映噪音覆盖时长。
- `segmentCount`：独立噪音事件数，反映被打断频率。

### 评分思路

评分从 100 分开始，依据三类惩罚项扣分：

- 持续噪音惩罚
- 超阈值时长惩罚
- 打断频率惩罚

常见固定参数：

- 帧间隔：50ms
- 切片时长：30s
- 评分阈值：`-50 dBFS`
- 事件合并窗口：500ms
- 每分钟最大容忍事件数：6

### 数据流

1. 麦克风采集音频。
2. 计算 RMS 与 dBFS。
3. 聚合为时间切片。
4. 为每个切片计算统计值和评分。
5. 写入本地历史数据。
6. 在 UI 中展示实时状态与历史报告。

### 设计边界

- 评分使用原始统计口径，不应被显示层校准参数反向影响。
- 历史数据只存统计值，不存原始音频。
- 该系统用于环境质量评估，不作为专业声学测量工具。

## English

This document summarizes the noise monitoring and scoring model used by LanMountainDesktop.

### Main metrics

- `p50Dbfs`: sustained noise level
- `overRatioDbfs`: ratio of time above threshold
- `segmentCount`: number of distinct interruption events

### Pipeline

1. capture microphone input
2. compute RMS and dBFS
3. aggregate frames into slices
4. score each slice
5. persist historical statistics
6. present realtime and historical views
