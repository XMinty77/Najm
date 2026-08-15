namespace Najm.Core.Tests.SceneGraph;

[TestClass]
public sealed class NodeTests
{
    [TestMethod]
    public void GenericAddReturnsChildAndPreservesInsertionOrder()
    {
        var parent = new TestNode();
        var first = new SpecializedNode();
        var second = new TestNode();
        var third = new TestNode();

        var returned = parent.Add(first);
        parent.Add(second);
        parent.Add(third);

        Assert.AreSame(first, returned);
        Assert.AreSame(parent, first.Parent);
        Assert.HasCount(3, parent.Children);
        Assert.AreSame(first, parent.Children[0]);
        Assert.AreSame(second, parent.Children[1]);
        Assert.AreSame(third, parent.Children[2]);
        Assert.IsFalse((object)parent.Children is ICollection<Node>);
    }

    [TestMethod]
    public void RemoveUsesIdentityAndReinsertAppendsAtEnd()
    {
        var parent = new TestNode();
        var first = parent.Add(new TestNode());
        var second = parent.Add(new TestNode());
        var third = parent.Add(new TestNode());

        Assert.IsTrue(parent.Remove(second));
        Assert.IsNull(second.Parent);
        Assert.IsFalse(parent.Remove(second));

        parent.Add(second);

        Assert.AreSame(first, parent.Children[0]);
        Assert.AreSame(third, parent.Children[1]);
        Assert.AreSame(second, parent.Children[2]);
    }

    [TestMethod]
    public void DuplicateAndMultipleParentsFailWithoutChangingTree()
    {
        var firstParent = new TestNode();
        var secondParent = new TestNode();
        var child = firstParent.Add(new TestNode());

        Assert.ThrowsExactly<InvalidOperationException>(() => firstParent.Add(child));
        Assert.ThrowsExactly<InvalidOperationException>(() => secondParent.Add(child));

        Assert.AreSame(firstParent, child.Parent);
        Assert.HasCount(1, firstParent.Children);
        Assert.IsEmpty(secondParent.Children);
    }

    [TestMethod]
    public void SelfAndAncestorCyclesFailWithoutChangingTree()
    {
        var root = new TestNode();
        var child = root.Add(new TestNode());
        var grandchild = child.Add(new TestNode());

        Assert.ThrowsExactly<InvalidOperationException>(() => root.Add(root));
        Assert.ThrowsExactly<InvalidOperationException>(() => grandchild.Add(root));

        Assert.IsNull(root.Parent);
        Assert.AreSame(root, child.Parent);
        Assert.AreSame(child, grandchild.Parent);
    }

    [TestMethod]
    public void TransformlessAndTwoDimensionalNodesCannotBridgeSpaces()
    {
        var transformlessParent = new TestNode();
        var twoDimensionalParent = new Node2D();
        var transformlessChild = new TestNode();
        var twoDimensionalChild = new Node2D();

        Assert.ThrowsExactly<InvalidOperationException>(() => transformlessParent.Add(twoDimensionalChild));
        Assert.ThrowsExactly<InvalidOperationException>(() => twoDimensionalParent.Add(transformlessChild));

        Assert.IsEmpty(transformlessParent.Children);
        Assert.IsEmpty(twoDimensionalParent.Children);
        Assert.IsNull(transformlessChild.Parent);
        Assert.IsNull(twoDimensionalChild.Parent);
    }

    private class TestNode : Node;

    private sealed class SpecializedNode : TestNode;
}
