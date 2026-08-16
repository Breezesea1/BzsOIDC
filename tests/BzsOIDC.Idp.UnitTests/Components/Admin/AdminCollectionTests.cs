using BzsOIDC.Idp.Components.Admin;

namespace BzsOIDC.Idp.UnitTests.Components.Admin;

public sealed class AdminCollectionTests
{
    [Fact]
    public void Query_WhenFilterSearchSortAndPaginationAreConfigured_AppliesAllQueryOperations()
    {
        var collection = new AdminCollection<TestRow, int>(static row => row.Id);
        collection.ReplaceRows(
        [
            new TestRow(1, "Beta"),
            new TestRow(2, "Alpha"),
            new TestRow(3, "Alpine"),
        ]);

        var result = collection.Query(new AdminCollectionQuery<TestRow>(
            PageIndex: 1,
            PageSize: 1,
            SortName: "Name",
            SortDescending: false,
            Filter: static row => row.Id != 1,
            SearchScorer: static row => row.Name.StartsWith("Al", StringComparison.Ordinal) ? 0 : null,
            Sort: static (rows, _, _) => rows.OrderBy(static row => row.Name, StringComparer.Ordinal)));

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal(1, result.PageStartIndex);
        Assert.Equal(1, result.PageEndIndex);
        Assert.Equal("Alpha", Assert.Single(result.Items).Name);
    }

    [Fact]
    public void Query_WhenRequestedPageExceedsResults_ClampsToLastPage()
    {
        var collection = new AdminCollection<TestRow, int>(static row => row.Id);
        collection.ReplaceRows([new TestRow(1, "Alpha"), new TestRow(2, "Beta")]);

        var result = collection.Query(new AdminCollectionQuery<TestRow>(3, 1, null, false));

        Assert.Equal(2, result.CurrentPage);
        Assert.Equal("Beta", Assert.Single(result.Items).Name);
    }

    [Fact]
    public void Query_WhenNoRowsMatch_ReportsSingleEmptyPage()
    {
        var collection = new AdminCollection<TestRow, int>(static row => row.Id);
        collection.ReplaceRows([new TestRow(1, "Alpha")]);

        var result = collection.Query(new AdminCollectionQuery<TestRow>(4, 10, null, false, static _ => false));

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(1, result.TotalPages);
        Assert.Equal(1, result.CurrentPage);
        Assert.Equal(0, result.PageStartIndex);
        Assert.Equal(0, result.PageEndIndex);
    }

    [Fact]
    public void ReplaceRows_WhenUnderlyingRowsChange_ReconcilesSelectionAgainstRowKeys()
    {
        var collection = new AdminCollection<TestRow, int>(static row => row.Id);
        var alpha = new TestRow(1, "Alpha");
        var beta = new TestRow(2, "Beta");
        collection.ReplaceRows([alpha, beta]);
        collection.ReplaceSelection([alpha, beta]);

        collection.Query(new AdminCollectionQuery<TestRow>(1, 10, null, false, static row => row.Id == 1));
        Assert.Equal(2, collection.SelectedItems.Count);

        collection.ReplaceRows([new TestRow(2, "Beta updated")]);

        var selected = Assert.Single(collection.SelectedItems);
        Assert.Equal(2, selected.Id);
        Assert.Equal("Beta updated", selected.Name);
    }

    private sealed record TestRow(int Id, string Name);
}
