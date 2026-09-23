using System.Runtime.CompilerServices;

// 宿主与测试需要访问 sqlite-vec 扩展加载器（内部实现细节，不对外公开 API）。
[assembly: InternalsVisibleTo("openLuo")]
[assembly: InternalsVisibleTo("openLuo.Tests")]
