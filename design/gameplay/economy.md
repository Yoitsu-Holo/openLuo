# 经济系统

## 1. 当前范围

当前经济系统是轻量玩法子域，重点覆盖：

- 物品目录
- 商店浏览与购买
- 礼物使用
- 背包查询与增减

## 2. 当前数据来源

- 物品定义主要来自 `openLuo/data/item-packs/*.jsonc`
- 商店与背包能力通过宿主服务和插件共同提供

## 3. 当前主要服务

- `ShopService`
- `GiftService`
- `ItemDefinitionCatalog`
- `ShopOfferRepository`
- 背包相关仓储接口

## 4. 当前 API 面

通过 `game/*` 可见的经济相关接口包括：

- `game/inventory/get`
- `game/inventory/add`
- `game/inventory/remove`
- `game/items/list`
- `game/shop/categories`
- `game/shop/list`
- `game/shop/buy`
- `game/gift/execute`

## 5. 当前设计特点

- 经济不是独立复杂数值系统，而是围绕角色互动与礼物反馈服务。
- 物品定义是内容驱动的，可通过 mod 扩展。
- 商店和礼物已具备宿主服务与插件桥接闭环。

## 6. 当前欠缺

- 尚未形成更完整的货币、产出、消耗和平衡设计
- 经济与世界状态、角色偏好之间还有较大扩展空间
