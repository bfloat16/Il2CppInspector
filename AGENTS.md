# 后续开发规范

本文件是仓库的根规范，适用于所有目录。`.editorconfig`、`.csharpierrc.json` 和 `.gitattributes` 是格式的最终依据；本文件与它们冲突时，以配置文件为准，并同步修正本文件。

## 1. 环境与构建

- `Il2CppInspector.slnx`：.NET 10 解决方案；`VersionedSerialization.Generator` 是 Roslyn 源生成器，目标为 `netstandard2.0`，只能使用该框架可用的 API。
- 除 `VersionedSerialization*` 外，项目均为 `Nullable disable`、`ImplicitUsings enable`。不要在禁用可空的项目里零散添加 `?` 注解，也不要重复引入隐式命名空间。
- Release 构建必须 0 警告、0 错误。唯一的既有例外是完整重建时 `VersionedSerialization.Generator` 的 RS2008。
- 新增插件项目时：
  - 目录 `Il2CppInspector.Plugin/<GameId>/`，项目名 `Il2CppInspector.Plugin.<GameId>.csproj`（即程序集名），设置 `EnableDynamicLoading=true`、引用 Common，并显式设置 `RootNamespace` 为 `Il2CppInspector.Plugin.<game>`；
  - 加入 `Il2CppInspector.slnx` 的 `/Plugins/<game>/` 解决方案文件夹，同一游戏的各版本放在同一文件夹下；
  - 打包由 `Il2CppInspector.Plugin/Directory.Build.targets` 自动处理，不需要改 CLI。

## 2. 目录

| 路径 | 职责 |
| --- | --- |
| `Il2CppInspector.CLI` | 命令行入口、输出目标选择、插件发现与进度显示 |
| `Il2CppInspector.Common/IL2CPP` | 元数据与二进制加载、版本判断、注册结构扫描 |
| `Il2CppInspector.Common/Next` | 版本化元数据与二进制记录定义 |
| `Il2CppInspector.Common/FileFormatStreams` | PE/ELF/Mach-O/NSO 等文件格式读取 |
| `Il2CppInspector.Common/Architectures` | 各架构的注册结构定位 |
| `Il2CppInspector.Common/Reflection` | 仿 System.Reflection 的类型模型 |
| `Il2CppInspector.Common/Cpp` | C/C++ 类型、声明生成与 Unity 头文件 |
| `Il2CppInspector.Common/Model` | 应用模型与原生布局分析 |
| `Il2CppInspector.Common/Outputs` | DummyDll、C#、C++、JSON、脚本、PDB、DWARF 输出 |
| `Il2CppInspector.Common/Plugins` | 插件契约：`GamePlugin`、`GameMetadataAdapter`、`GameNativeModel` 等 |
| `Il2CppInspector.Plugin/<GameId>` | 单个游戏版本的插件 |
| `Il2CppInspector.Plugin/Shared/<family>` | 多个游戏共用的插件源码（如 MORAX：BH3、Genshin、HSR、ZZZ），由各插件项目通过 `.props` 编入 |
| `Il2CppInspector.Tests` | 集成测试控制台程序 |
| `Bin2Object`、`VersionedSerialization*` | 二进制对象读写与版本化序列化 |

| 游戏 | GameId |
| --- | --- |
| BH3 | `BH3_CN_Windows_9.1.0` |
| Genshin | `Genshin_CN_Windows_7.1.0` |
| HSR | `HSR_OS_Windows_4.6.0`、`HSR_CN_Windows_4.6.51` |
| ZZZ | `ZZZ_CN_Windows_3.2.0`、`ZZZ_CN_Windows_3.3.0` |
| Endfield | `Endfield_CN_Android_x.x.x`、`Endfield_CN_Windows_1.6.3` |
| TOT | `TOT_CN_Android_6.1.0`、`TOT_OS_Android_5.8.1` |
| 恋与深空 | `LAD_CN_Android_6.0.0` |

## 3. 插件开发

- `GameId` 格式为 `NAME_REGION_PLATFORM_VERSION`，与插件目录名完全一致；`x.x.x` 表示支持多个游戏版本。
- 插件实现公开、无参构造的 `GamePlugin` 子类。`Matches` 必须能区分同一游戏的不同版本；自动识别有歧义时 CLI 要求显式 `--game`。
- 分层：
  - Common 不引用任何游戏项目，不硬编码游戏的地址、密钥或布局；
  - 插件只负责解码、规范化记录和经二进制验证的运行时布局差异；反射模型、声明生成、依赖排序和所有导出复用 Common；
  - 不另写 DLL、头文件或 JSON 输出器；`WriteAssemblies`/`WriteHeader`/`WriteJson` 与基类相同时不要重写。
- 加密或重排的记录先规范化为 stock 记录，再通过 `Metadata.CreateForPlugin` 挂接 `GameMetadataAdapter`。Common 的 `internal` 成员已对所有插件项目开放，不要为插件把成员改为 `public`。
- 运行时布局：只提供不同于 stock 的布局钩子；`NativeLayoutAnalysisModel` 负责方法遍历与声明命名。字段宽度、缓存记录或 vtable 表示的改动须有二进制证据，并在注释中写明逆向地址。
- 导出命名沿用 stock 规则：保留元数据中的类型、方法和字段名，不加游戏前缀（如 `Zzz_`）。
- 同一引擎分支的多个游戏（如 MORAX）共用 `Il2CppInspector.Plugin/Shared/<family>/` 下的基类与辅助代码，项目通过 `<Import Project="../Shared/<family>/<family>.props" />` 编入；游戏目录只放密钥、偏移、记录读取器和确有差异的布局。
- 插件目录统一为：`<Game>Plugin.cs`、`<Game>Build.cs`（版本常量，可选）、`IL2CPP/`（适配器、二进制、默认值、运行时头）、`Model/`、`Next/Metadata/<Game>Records.cs`、`Reflection/`，不再按游戏名嵌套子目录。
- 同一游戏的多个版本共享相同源码时，用 `<Compile Include="../<GameId>/..." Link="..." />` 链接，不复制文件（参考 `TOT_OS_Android_5.8.1`、`ZZZ_CN_Windows_3.3.0`）；某版本需要不同实现时，再把该文件复制到本版本目录并去掉链接。
- 嵌入资源的 `LogicalName` 固定为 `Il2CppInspector.Plugin.<game>.<文件名>`；共享资源沿用源版本或共享族的 `LogicalName`（如 `Il2CppInspector.Plugin.Morax.RuntimeCaches.py`）。
- 每个插件在 `Il2CppInspector.Plugin/README.md` 的支持表中登记，并在 `Il2CppInspector.Tests/Plugin/GameSampleTests.cs` 的 `Samples` 表中加一行（二进制文件名和基线）。

## 4. 代码风格

### 格式

- C# 使用 4 空格缩进、LF 换行、文件末尾换行，CSharpier 行宽 150；XML、项目文件、`.slnx`、`.props`、`.targets` 和 JSON 使用 2 空格。
- 所有文本文件为无 BOM 的 UTF-8 和 LF，不留行尾空白（Markdown 除外）。
- 提交前运行 `csharpier format .`。`dotnet format` 可能写入 CRLF，运行后须再执行一次 CSharpier。
- Python 脚本资源使用 4 空格缩进；`RuntimeCaches.py` 是插入 `shared_base.py` 占位符的片段，单独编译会报缩进错误，属于正常情况。

### 语言特性

- 类型明显或冗长时使用 `var`。
- 使用目标类型 `new()`、集合表达式 `[]` 和模式匹配（`is not null`、属性模式、`switch` 表达式）。
- 新文件使用文件作用域命名空间；既有文件不为此单独改动。
- `using` 放在命名空间外，System 优先，删除未使用的 `using`；不写 `this.`。
- 只在构造时赋值的字段标记 `readonly`；无状态的局部函数和 lambda 标记 `static`。
- 单行语句体可以省略花括号，多行语句体必须带花括号；修改已有代码时保持该文件的写法。
- 表达式体只用于属性、索引器、访问器和单表达式的简短成员。

### 命名

| 符号 | 规则 |
| --- | --- |
| 类型、命名空间、方法、属性、事件、公共字段、常量 | PascalCase |
| 接口 / 类型参数 | `I` 前缀 / `T` 前缀 |
| 参数、局部变量 | camelCase |
| 私有实例字段 | `_camelCase` |
| 私有静态字段 | `s_camelCase` |
| 私有 `static readonly` 字段、私有常量 | PascalCase |

- 既有文件的私有字段多为无前缀 camelCase：修改时沿用该文件的风格，不顺带批量重命名；新文件按上表。
- 既有的公共名称（如 `JSONMetadata`）保持不变，避免破坏插件 API。

### 注释

- 注释说明原因、格式依据和不明显的约束，不复述代码。二进制格式、偏移、魔数和版本判断写明对应的 IL2CPP/Unity/游戏版本或逆向地址（如 `0x19F266500`）。
- 公共契约使用 `/// <summary>`；私有实现不强制。
- 注释使用该文件已有的语言。

## 5. 简洁性：不写与要删除的代码

以下写法不得新增；发现时删除或修改，前提是输出不变。

- **冗余代码**：未使用的私有成员、参数、局部变量和 `using`；不可达分支；注释掉的代码；描述已完成工作的 TODO；与现有帮助方法重复的逻辑。删除非私有成员前，用 grep 核对 Common、CLI、所有插件和 Tests（包括按字符串名称的反射调用）。
- **无用封装**：只转发调用、不增加语义的方法或类；只有一个实现、也没有替换需求的新接口；与基类实现完全相同的重写。
- **过度防御**：对不可能为 null 的值判空，或对非空值使用 `?.`/`??`；调用方和被调用方重复校验；重复检查已保证的条件（如 `Initialize` 已拒绝的版本）；吞掉异常或原样重抛的 `try/catch`。
- **无意义赋值**：字段显式初始化为默认值；读取前就被覆盖的赋值；只为立即 `return` 而存在的临时变量；只写不读的字段。只需跳过的字段用 `_ = reader.Read...()` 读过。
- **冗余转换**：多余的强制转换和括号；对 `ulong` 做 `& ulong.MaxValue` 之类无效运算。

以下内容属于必要校验，**必须保留**：

- 签名、魔数、版本字段、长度、偏移、计数、索引范围的校验；
- `MetadataUsages ?? []` 等对低版本元数据（< 19）缺失数据的处理；
- 不同目标工具或工具版本之间的 Python 兼容分支；
- 结构体字段顺序、`[Version]` 特性、偏移量、密钥、白盒表和版本识别阈值——这些由二进制证据决定，改动须在提交说明中附证据；
- 记录格式来源的注释。

## 6. 测试

- `Il2CppInspector.Tests` 是控制台程序，按模式分发（`Program.cs`）。新增模式须同时更新 `Program.cs` 的用法说明和对应 README。
- 目录与生产代码对应：通用逻辑放 `Common/`，CLI 流程放 `CLI/`。
- 所有游戏走同一套测试，不为单个游戏写专属测试或模式：`--plugins` 对每个已安装插件做相同的选择与拒绝检查；`--game <GameId> <样本目录> [输出目录]` 在真实样本上核对解码表大小和运行时布局基线，给出输出目录时再写 DummyDll 并验证引用解析。解码或布局变化导致基线改变时，须在提交说明中附逆向证据。
- 断言使用 `TestAssert.Check`，只记录通过时用 `TestAssert.Pass`；仓库路径、fixture 路径和外部进程使用 `TestEnvironment`。测试中不捕获异常，失败直接终止。
- 只通过公共契约（`GamePlugin`、`Il2CppInspector`、`TypeModel`、`AppModel`）测试插件，不访问插件内部成员，也不使用反射。
- 以下测试不要写：重复断言同一输入的同一结果；同义反复或 `Check(true, ...)`；只验证刚构造的对象非 null；被更强断言覆盖的格式细节；简单 getter。

## 7. 验证

在仓库根目录执行：

```bash
csharpier format .
csharpier check .
dotnet build Il2CppInspector.slnx -c Release
# 不需要本地游戏文件的测试
dotnet run --project Il2CppInspector.Tests -c Release --no-build -- --plugins
dotnet run --project Il2CppInspector.Tests -c Release --no-build -- --progress
dotnet run --project Il2CppInspector.Tests -c Release --no-build -- --cli-targets Il2CppInspector.CLI/bin/Release/net10.0/Il2CppInspector.dll
dotnet run --project Il2CppInspector.Tests -c Release --no-build -- --cpp-layout
python -I Il2CppInspector.Tests/Common/Outputs/ScriptResourcesTests.py
# 修改插件时在真实样本上运行
dotnet run --project Il2CppInspector.Tests -c Release --no-build -- --game <GameId> <样本 Native 目录> [输出目录]
```

- 修改输出或解码逻辑时，用修改前后的 CLI 在真实样本上对比输出：PDB/DWARF 应逐字节相同，`il2cpp.json` 与 `dump.cs` 应相同。C# 输出与 DummyDll 受并行导出顺序和 MVID 影响，每次运行都会不同，不能用来判断是否回归。

## 8. 交付

- 保留工作树中已有的未提交改动：不执行 `git checkout`/`restore`/`reset --hard`/`clean` 等会丢弃改动的命令；除非明确要求，不要提交。
- 不提交 `bin/`、`obj/` 和 `output/` 等生成内容。
- 支持的游戏、`GameId` 或 CLI 选项变化时，同步更新本文件第 2 节、`Il2CppInspector.Plugin/README.md` 和 CLI 帮助文本。
- 提交信息使用 Conventional Commits 格式：`type(scope): summary`。
  - `type` 取 `feat`、`fix`、`refactor`、`perf`、`test`、`docs`、`chore`；
  - `scope` 用模块（`outputs`、`cli`、`plugins`）或游戏代号（`zzz`、`tot`、`genshin`）；
  - `summary` 用英文祈使句，小写开头。
- 格式化、重构与行为变更分开提交。
- 交付时说明变更内容、验证结果（构建、测试计数）以及未完成的检查。
