using System.Runtime.CompilerServices;

// 应用层（内核装配）与测试需要访问 sqlite-vec 扩展加载器（内部实现细节，不对外公开 API）。
[assembly: InternalsVisibleTo("openLuo.Application")]
[assembly: InternalsVisibleTo("openLuo.Tests")]
