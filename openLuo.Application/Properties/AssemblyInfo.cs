using System.Runtime.CompilerServices;

// 应用层测试需要访问内部实现（原宿主 exe 的该授权随代码一起迁移过来）。
[assembly: InternalsVisibleTo("openLuo.Tests")]
