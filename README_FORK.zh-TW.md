# Veyra 修改版（非官方）

本倉庫是 [Likely7/Veyra-NRVideo](https://github.com/Likely7/Veyra-NRVideo) 的**非官方修改版**，以 **GPL-3.0** 授權再發布。

請先閱讀：

- [NOTICE.md](NOTICE.md) — 來源聲明、修改說明、第三方限制
- [LICENSE](LICENSE) — GPL-3.0
- [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) — 第三方聲明

上游官方專案與發行版請以原倉庫為準。本倉庫**不附帶** NVIDIA SDK、便攜包內專有運行庫／模型。

## 本叉主要改動

1. 雙卡同型號：按 PCI bus 編號標「一号／二号」（測試主機：上槽 bus 較小 = 一号，列表一号在上），並修正設置勾選／綁定相關問題  
2. 螢幕擷取：幀率上限可自訂數字（0–240），開始擷取前會寫入設定  
3. 視窗擷取「跟隨」：依視窗畫面實際更新即時追蹤幀率（非顯示器刷新率）；擷取顯示器時仍為「跟隨顯示器」  
4. 原畫／增強分屏：分割位置 0–100% 拉桿、原畫／增強換邊開關（畫面拖曳仍可用）  
5. 右上角常駐視窗控制：最小化／最大化／關閉；最大化為整窗含功能欄（貼齊工作區），與畫面全螢幕（F11）分開  
6. 可選工具原始碼：`tools/gpu-bind/`（指定運算顯卡輔助）

## 編譯

請參考上游文件（如 `docs/BUILD_2.1.6_*.md`、`docs/DEVELOPMENT.md`），並自行準備 Qt 6.8.3、FFmpeg、NVIDIA／Intel／AMD SDK 等依賴。`third_party_local/README.md` 說明本倉庫刻意未收錄的 SDK 目錄。

## 聲明

非官方、與原作者無隸屬關係。轉載或再發布時請保留 LICENSE、NOTICE 與第三方聲明，並標明上游來源。
