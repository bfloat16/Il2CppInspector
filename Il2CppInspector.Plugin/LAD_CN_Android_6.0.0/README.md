# LAD_CN_Android_6.0.0

输入为 CN 6.0.0 ARM64 `libil2cpp.so`（SHA-256 `a94ecf27205457d1ac7c621157cbe7fc3dde153ab037ce50b38f9a57f2255bba`）和同一 APK 的 `global-metadata.dat`。元数据在内存中规范化为 v29，二进制注册结构使用 v29 读取器，运行时 Unity 版本为 `2019.4.34f1`。

| 内容 | 格式 | 依据 |
| --- | --- | --- |
| 注册函数 | `0x24F1344` 接收两个 registration 和 CodeGenOptions | `0x1C4F910` 跳板装载 X0/X1/X2 |
| CodeRegistration | v29 布局；+`0x30` invoker，+`0x38` unresolved calls，+`0x48` interop | `0x24F244C`、`0x24F13A4` |
| 泛型方法表 | MR+`0x28`，`{spec, method, invoker, adjustor}`，步长 `0x10` | `0x2474390` |
| 字符串堆 | hdr+`0x38`，XOR `0x34B0E` | `0x2471EF0`、`0x2473BAC` |
| 默认值 | 字段默认值明文，参数默认值 XOR `0x1C13`，值使用压缩编码 | `0x2473870`、`0x2473988`、`0x2489CB8` |
| 方法 flags | +`0x18` XOR `0x686` | `0x2473C48` |
| vtable token | 高三位区分 MethodDef/MethodRef，索引 `(token >> 1) & 0x0FFFFFFF` | `0x2472788` |
| 自定义属性 | hdr+`0xD0` 的 `{token, offset}` 指向 hdr+`0x68` 的数据 | `0x24732D4`、`0x248F0AC` |
| metadata usage | `.data` 中的全局槽保存奇数 token | `0x2472594` |
| 运行时头文件 | MethodInfo +`0x08` virtual pointer、+`0x10` invoker、+`0x30` 参数数组；vtable 起点 `0x120`，步长 16 | `0x24EE764`、`0x2471EF0` |
| CodeGenModule | 完整 v29 记录，间距 `0x88` | `0x9C0DA70` |

尚未还原：

- 44,759 个 GenericClass 的 cached_class 是运行时才填充的缓存，静态为零；这些泛型实例按不透明类型处理。
- 多数 section 的大小由相邻偏移推算；header 的 `0x70` 槽用途未知。
