# collection-ui（复制件）

本目录的代码复制自 collection-ui，作为 beeps 的一部分维护，可以自由修改。

- 来源仓库：https://github.com/NetworkCats/collection-ui （本地副本 `~/Developer/collection-ui`）
- 上游版本：`package.json` 中的 `0.1.0`
- 上游提交：**复制时上游仓库还没有任何提交**，无法记录提交号；上游首次提交后请在此补上对应的提交号
- 复制日期：2026-10-16
- 许可证：MIT（见 `LICENSE`）

## 复制的文件

上游路径均相对于 `src/scss/`，复制后目录结构不变，内容未修改：

| 文件 | 用途 |
|---|---|
| `abstracts/*` | 配置、函数、mixin（不输出 CSS） |
| `foundation/*` | 调色板、语义 token、reset、元素样式、布局原语 |
| `components/_button.scss` | 按钮 |
| `components/_navbar.scss` | 顶部导航栏 |
| `components/_footer.scss` | 页脚 |
| `components/_form.scss` | 表单字段、输入框、单选 |
| `components/_alert.scss` | 提示条 |
| `components/_card.scss` | 卡片 |
| `components/_copy-box.scss` | 可复制的 key / 提示语 / 邀请码 |
| `components/_badge.scss` | 状态标签 |
| `components/_avatar.scss` | 头像 |
| `components/_empty.scss` | 空状态 |
| `components/_table.scss` | 管理后台表格 |
| `components/_nav.scss` | 管理后台导航 |
| `components/_timeline.scss` | 分步骤组件（Stepper，`.steps`），用于 onboarding |
| `_utilities.scss` | 工具类（`visually-hidden` 等） |

只复制用到的组件；新增组件时从上游复制对应文件并更新上表。

## 与上游的差异

- beeps 不在这里改上游文件，而是通过 Sass 配置（`Client/scss/main.scss` 中的 `@use … with (…)`）
  和 `Client/scss/_tokens.scss` 中的 token 覆盖来调整设计，方便与上游对比。
- 不使用 Material Symbols 图标字体（不加载第三方字体），需要图标时使用内联 SVG。依赖图标字体的
  地方在 `Client/scss/_site.scss` 中覆盖：Stepper 已完成步骤的勾改用普通字符 `✓`；复制按钮
  一律带文字（`.btn-copy` 非空时上游会隐藏图标）。
- 脚本没有复制上游的 `src/ts/`：`Client/ts/copy.ts` 参照上游 `copy.ts` 改写（去掉 toast 和
  图标切换，按钮改为切换文字），`data-cui-copy` 改为 `data-copy`。

同步上游时，用上游对应提交的 `src/scss/` 与本目录逐个文件 diff。
