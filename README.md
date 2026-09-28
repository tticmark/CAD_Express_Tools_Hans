# AutoCAD Express Tools 汉化工具

把 AutoCAD **Express Tools（快捷工具）** 从英文汉化为简体中文的 Windows 桌面工具。
AutoCAD 主程序已是中文，本工具**只处理 Express Tools**，不会触碰其它功能。

目标版本：**AutoCAD 2026**（同时兼容 2020–2027，程序会自动探测）。

---

## 汉化范围

| 目标 | 说明 |
| --- | --- |
| `acetmain.cuix`（用户配置副本） | 下拉菜单、功能区面板与按钮、工具栏、命令名与命令说明（约 260 条） |
| `acetmain.cuix`（UserDataCache 源模板） | **必须一并汉化**：AutoCAD 启动时会用它重建用户副本，不改则菜单重启后变回英文 |
| `Express\*.dcl` | 对话框的 `label` / `text` / `title` 显示文本（约 100 条） |
| `Express\*.lsp` | `prompt` / `princ` / `alert` / `getstring` / `getkword` / `getpoint` 等界面文本（约 700 条） |

**不会改动**：AutoCAD 主程序文件、其它 CUIX、`.arx` / `.dll` 二进制资源、
`.fas`（已编译加密，如 `acetutil*.fas`）、`.lay`、`.dwg`，
以及 LSP 中的函数名、命令名、`(command ...)` 参数和 `initget` 关键字。

内置离线词典共 **1014 条**译文，实测覆盖率约 **94%**（1075 条中命中 1010 条）；
未覆盖的 65 条多为标点片段、ASCII 表格与开发调试字符串，保留英文原文，
并在界面中标为「未翻译」，可人工补录后导出/导入。

扫描时若发现文件中**已是中文**，会自动标记为「已汉化」并跳过，不会重复计入「未翻译」。

---

## 使用步骤

1. **关闭 AutoCAD**（程序会检测 `acad.exe`，运行中会拒绝写入）。
2. 运行 `CAD_ET_HANS.exe`，程序自动检测安装目录；未检测到时用「手动选择」指定
   `C:\Program Files\Autodesk\AutoCAD 2026`。
3. 勾选需要的汉化范围（默认全选）。
4. 点 **扫描** 读取词条（**只读，不写入任何文件**），在「翻译表」中逐条修改译文、
   禁用某条、搜索筛选。
   勾选「启动后自动扫描」后，程序启动即自动执行这一步。
5. 点击 **开始汉化**（若翻译表为空会自动先扫描）。写入前自动备份全部原文件，
   完成后**重启 AutoCAD** 生效。
6. 需要回到英文时点击 **一键还原**。

> 若结果里「替换字符串：0」，先看底部日志：正常应出现
> `扫描结果：共 N 条　已翻译 M` 与 `完成：修改 X 个文件，替换 Y 处`。
> 日志同时写入 `%LOCALAPPDATA%\CAD_ET_HANS\logs\app.log`。

### 管理员权限（重要）

`Express\*.dcl` / `*.lsp` 位于 `C:\Program Files\...` 下，**必须管理员权限才能写入**。
程序会在写入前实测目录权限：不可写时弹出提示，可选「以管理员身份重新启动本程序」。
未提权时只会得到大量「拒绝访问」错误（CUIX 仍可写入，因为它通常在用户目录）。

### 作用位置

- 用户配置目录下的 `acetmain.cuix`
  （`%APPDATA%\Autodesk\AutoCAD 2026\R26.x\chs\Support\acetmain.cuix`）——立即生效。
- `UserDataCache\Support\acetmain.cuix` 源模板（默认勾选「同时汉化 CUIX 源模板」）。
  **AutoCAD 启动时用它重建用户副本**，只改用户那份会在重启后失效。
- 两者都在 Program Files 或需提权时，程序会写入前实测权限并提示提权重启。

### 编码

LSP / DCL 默认以 **GB18030**（代码页 936）写回，与 AutoCAD 读取习惯一致。
若出现乱码，勾选「文本使用 UTF-8(BOM) 保存」后重新汉化。

### 强制本地化

CUI 中的显示节点带 `xlate="true"` + `UID="XLS_xxxx"`。若汉化后菜单仍显示英文，
保持勾选「强制本地化」，程序会把被改节点的 `xlate` 置为 `false`。

---

## 备份与还原

- 备份目录：`%LOCALAPPDATA%\CAD_ET_HANS\backups\<时间戳>\`，每次独立，不覆盖。
- 目录内 `manifest.json` 记录原路径与 SHA256，还原时校验后写回。
- 主界面「一键还原」还原最近一次备份；也可用「打开备份目录」手动取回原文件。

---

## 项目结构

```
src\CAD_ET_HANS.Core\        纯逻辑类库（无 UI 依赖）
  Models\                    条目、环境、备份清单模型
  Services\
    AcadLocator.cs           安装目录 / Express 目录 / CUIX 候选路径探测
    CuixPatcher.cs           CUIX 解压、选择性替换、无损重打包
    TextFilePatcher.cs       DCL 与 LSP 规则化替换
    DictionaryService.cs     内置词典、用户覆盖词典、导入导出
    BackupService.cs         快照备份与还原
    LocalizationEngine.cs    扫描 → 查词典 → 打补丁 编排
  Resources\dictionary.*.json  内置离线词典
src\CAD_ET_HANS.App\         WPF 桌面应用（net8.0-windows）
tools\CAD_ET_HANS.Extract\   开发期工具：导出全部英文原文，用于维护词典
```

## 编译

```powershell
dotnet build -c Release
dotnet publish src\CAD_ET_HANS.App -c Release -o publish
```

需要 .NET 8 SDK（`net8.0-windows`，WPF）。

## 维护词典

```powershell
dotnet run --project tools\CAD_ET_HANS.Extract -- "C:\Program Files\Autodesk\AutoCAD 2026" strings.tsv
```

输出的 `strings.tsv` 中 `DictionaryHit = N` 的行即未覆盖条目，
译好后通过主界面「导入」写入用户词典
（`%LOCALAPPDATA%\CAD_ET_HANS\user-dictionary.json`，优先级高于内置词典），
或直接补充 `src\CAD_ET_HANS.Core\Resources\dictionary.lsp.json` 等文件并重新编译。
