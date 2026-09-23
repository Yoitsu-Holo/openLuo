using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Capabilities.Infrastructure;
using Xunit;

namespace openLuo.Capabilities.Tests;

public class InMemoryStateTransactionTests
{
    [Fact]
    public async Task Commit_WithMatchingVersion_CommitsAtomically()
    {
        var tx = new InMemoryStateTransaction();

        var result = await tx.CommitAsync("subject-1", baseVersion: 0,
        [
            new MutationIntent { SubjectId = "subject-1", ResourcePath = "mood", Op = MutationOp.Set, Value = "happy" },
            new MutationIntent { SubjectId = "subject-1", ResourcePath = "energy", Op = MutationOp.Set, Value = 80 }
        ]);

        Assert.Equal(MutationBatchStatus.Committed, result.Status);
        Assert.NotNull(result.NewSnapshot);
        Assert.Equal(1, result.NewSnapshot!.Version);
        Assert.Equal("happy", result.NewSnapshot.Values["mood"]);
        Assert.Equal(80, result.NewSnapshot.Values["energy"]);
    }

    [Fact]
    public async Task Commit_WithStaleVersion_ReturnsConflict()
    {
        var tx = new InMemoryStateTransaction();
        await tx.CommitAsync("subject-1", baseVersion: 0,
        [
            new MutationIntent { SubjectId = "subject-1", ResourcePath = "mood", Op = MutationOp.Set, Value = "happy" }
        ]);

        // 第二次提交使用旧版本号（当前应为 1）
        var result = await tx.CommitAsync("subject-1", baseVersion: 0,
        [
            new MutationIntent { SubjectId = "subject-1", ResourcePath = "energy", Op = MutationOp.Set, Value = 1 }
        ]);

        Assert.Equal(MutationBatchStatus.Conflict, result.Status);
        Assert.NotNull(result.Conflicts);
        Assert.Contains("energy", result.Conflicts);
    }

    [Fact]
    public async Task Commit_SameResourceTwiceInBatch_ReturnsConflict()
    {
        var tx = new InMemoryStateTransaction();

        var result = await tx.CommitAsync("subject-1", baseVersion: 0,
        [
            new MutationIntent { SubjectId = "subject-1", ResourcePath = "mood", Op = MutationOp.Set, Value = "a" },
            new MutationIntent { SubjectId = "subject-1", ResourcePath = "mood", Op = MutationOp.Set, Value = "b" }
        ]);

        Assert.Equal(MutationBatchStatus.Conflict, result.Status);
    }

    [Fact]
    public async Task Commit_Increment_Accumulates()
    {
        var tx = new InMemoryStateTransaction();
        await tx.CommitAsync("subject-1", baseVersion: 0,
        [
            new MutationIntent { SubjectId = "subject-1", ResourcePath = "gold", Op = MutationOp.Increment, Value = 10 }
        ]);

        var result = await tx.CommitAsync("subject-1", baseVersion: 1,
        [
            new MutationIntent { SubjectId = "subject-1", ResourcePath = "gold", Op = MutationOp.Increment, Value = 5 }
        ]);

        Assert.Equal(MutationBatchStatus.Committed, result.Status);
        Assert.Equal(15, result.NewSnapshot!.Values["gold"]);
    }
}
