# collection-ui（复制件）

本目录的代码复制自 collection-ui，作为 beeps 的一部分维护，可以自由修改。

- 来源仓库：https://github.com/NetworkCats/collection-ui （本地副本 `~/Developer/collection-ui`）
- 上游版本：`package.json` 中的 `0.1.0`
- 上游提交：**复制时上游仓库还没有任何提交**，无法记录提交号；上游首次提交后请在此补上对应的提交号
- 复制日期：2026-10-16
- 许可证：MIT（见 `LICENSE`）

## 复制的文件

上游路径均相对于 `src/scss/`，复制后目录结构不变。2026-10-19 起各文件已裁剪到 beeps 实际用到的部分（见下文“与上游的差异”）：

| 文件 | 用途 |
|---|---|
| `abstracts/*` | 配置、函数、mixin（不输出 CSS） |
| `foundation/*` | 调色板、语义 token、reset、元素样式、布局原语 |
| `components/_button.scss` | 按钮 |
| `components/_navbar.scss` | 只保留跳到正文的 `.skip-link`（beeps 没有顶部导航栏） |
| `components/_form.scss` | 表单字段、输入框、单选 |
| `components/_alert.scss` | 提示条 |
| `components/_card.scss` | 卡片（新 key 的提示框） |
| `components/_copy-box.scss` | 可复制的 key / 提示语 / 邀请码 |
| `components/_badge.scss` | 状态标签 |
| `components/_avatar.scss` | 头像 |
| `components/_empty.scss` | 空状态 |
| `components/_table.scss` | 管理后台表格 |
| `components/_nav.scss` | 管理后台导航 |
| `components/_timeline.scss` | 只保留分步骤组件（Stepper，`.steps`），用于 onboarding；时间线部分已删 |
| `components/_tabs.scss` | 个人主页的"beep / 回复"标签（2026-10-16 阶段 3 复制，只用链接形式，不用 `indicator.js`） |
| `components/_dropdown.scss` | 导航栏账号菜单的菜单项样式（2026-10-16 阶段 3 复制；菜单本身用 `<details>`，不用 Popover 和 `dropdown.js`） |
| `components/_icon.scss` | 图标（Material Symbols，2026-10-17 UI 重构时复制） |
| `components/_search.scss` | 搜索框（侧栏和搜索页，2026-10-17 复制） |
| `components/_toast.scss` | 操作结果提示（2026-10-17 复制；不用 `toast.js`，见 `Client/app/shell/Toasts.tsx`） |
| `components/_dialog.scss` | 删除等操作的确认对话框（2026-10-17 复制；不用 `dialog.js`，见 `Client/app/shell/ConfirmDialog.tsx`） |
| `components/_skeleton.scss` | 内容加载前的骨架屏（2026-10-17 复制；页面的骨架见 `Pages/Shared/_Skeleton.cshtml`）。宽度不用上游的 `.w-50` 工具类和内联 `--size`，用 `_site.scss` 里的修饰类 |
| `_utilities.scss` | 只保留 `visually-hidden` |

只复制用到的组件；新增组件或用到已删的变体时，从上游复制对应部分并更新上表。

## 与上游的差异

- 设计上的调整通过 Sass 配置（`Client/scss/_settings.scss` 中的 `@use … with (…)`，`main.scss`、`member.scss`、`admin.scss`
  都先加载它）和 `Client/scss/_theme.scss` 中的 token 覆盖来做。
- 2026-10-19 按 beeps 的实际用量裁剪了所有文件：删掉没有用到的组件（页脚）、区块（navbar、时间线、banner、callout、chip、
  indicator、popover、drawer、布局原语等）、变体（如 `btn-success`、`badge-pill`、`avatar-xs`）、工具类，以及只配合上游 JS 的
  状态类和属性钩子（`is-dismissing`、`.active`、`data-cui-theming`、`data-cui-indicator`、`th[data-sort]`）。头注释只记录剩下的部分。
- token 也只输出被读到的：调色板只输出品牌色和各状态色相的明暗两阶（`brand-color()` 引用的），其余色阶在 Sass 里用
  `palette-color()` 取；`$statuses` 的 `tokens` 列出每个状态色要输出哪些派生 token；去掉了 `[data-brand]` 预设、`--cui-prefix`、
  布局 token 和用不到的间距、字号、阴影、缓动、层级。
- 图标字体用 Material Symbols Rounded（`$icon-font`），但自托管并裁剪到只含用到的图标：名单在
  `Client/icons.json`，由 `scripts/build-icons.ts` 下载到 `wwwroot/fonts/`（见 PLAN 6.9）。组件
  用 `icon-glyph()` 画的图标（Stepper 的勾、关闭按钮）也要列进名单；复制按钮一律带文字
  （`.btn-copy` 非空时上游会隐藏图标），所以不需要 `content_copy`。
- 中性灰改成无彩度的纯灰（`_settings.scss` 里覆盖 `$neutral`），并关掉品牌色对中性色的 tint
  （`_theme.scss`），页面背景是纯白 / 近黑。圆角比上游大（`$radii`），按钮是胶囊形、没有上游的
  键帽立体效果。
- 脚本没有复制上游的 `src/ts/`：复制按钮（`Client/app/components/KeyHandoff.tsx`、`Client/app/clipboard.ts`）
  参照上游 `copy.ts` 改写（去掉 toast 和图标切换，按钮改为切换文字）。

同步上游时，用上游对应提交的 `src/scss/` 与本目录逐个文件 diff，只取 beeps 用到的部分。
