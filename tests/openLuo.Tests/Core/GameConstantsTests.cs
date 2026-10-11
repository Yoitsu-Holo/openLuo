using openLuo.Core;
using openLuo.Core.Models;

namespace openLuo.Core.Tests;

public sealed class GameConstantsTests
{
    [Theory]
    [InlineData(0, RelationshipStage.Stranger)]
    [InlineData(199, RelationshipStage.Stranger)]
    [InlineData(200, RelationshipStage.Acquaintance)]
    [InlineData(399, RelationshipStage.Acquaintance)]
    [InlineData(400, RelationshipStage.Friend)]
    [InlineData(599, RelationshipStage.Friend)]
    [InlineData(600, RelationshipStage.CloseFriend)]
    [InlineData(799, RelationshipStage.CloseFriend)]
    [InlineData(800, RelationshipStage.Lover)]
    [InlineData(1000, RelationshipStage.Lover)]
    public void GetRelationshipStageForAffection_UsesExpandedThresholds(int affection, RelationshipStage expected)
    {
        Assert.Equal(expected, GameConstants.GetRelationshipStageForAffection(affection));
    }
}
