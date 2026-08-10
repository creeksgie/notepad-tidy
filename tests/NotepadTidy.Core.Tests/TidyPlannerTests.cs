using NotepadTidy.Core;

namespace NotepadTidy.Core.Tests;

public class TidyPlannerTests
{
    private static TabRecord Note(string text, int dayOffset, string id) => new()
    {
        Id = new Guid(id),
        Status = TabStatus.Ok,
        Text = text,
        Created = new DateTime(2025, 1, 1).AddDays(dayOffset),
    };

    /// <summary>
    /// Chronology decides, never size. Picking the largest note as the
    /// container would scramble the order on the very first run — exactly when
    /// a user has the most notes to merge and the least trust in the tool.
    /// </summary>
    [Fact]
    public void Plan_ChoosesTheOldestNoteAsContainer()
    {
        var plan = TidyPlanner.Plan(
        [
            Note("#project" + new string('x', 5000), dayOffset: 30, "22222222-2222-2222-2222-222222222222"),
            Note("#project oldest and shortest", dayOffset: 0, "11111111-1111-1111-1111-111111111111"),
            Note("#project middle one", dayOffset: 10, "33333333-3333-3333-3333-333333333333"),
        ]);

        var group = Assert.Single(plan.Groups);
        Assert.Equal(new Guid("11111111-1111-1111-1111-111111111111"), group.Container);
    }

    [Fact]
    public void Plan_AppendsSourcesFromOldestToNewest()
    {
        var plan = TidyPlanner.Plan(
        [
            Note("#project newest", 30, "33333333-3333-3333-3333-333333333333"),
            Note("#project oldest", 0, "11111111-1111-1111-1111-111111111111"),
            Note("#project middle", 10, "22222222-2222-2222-2222-222222222222"),
        ]);

        var group = Assert.Single(plan.Groups);
        Assert.Equal(
        [
            new Guid("22222222-2222-2222-2222-222222222222"),
            new Guid("33333333-3333-3333-3333-333333333333"),
        ], group.Sources);
    }

    [Fact]
    public void Plan_ExcludesFileBackedTabs()
    {
        // A dev file opened in Notepad must never be read, merged or counted.
        var plan = TidyPlanner.Plan(
        [
            Note("#project one", 0, "11111111-1111-1111-1111-111111111111"),
            Note("#project two", 1, "22222222-2222-2222-2222-222222222222"),
            new TabRecord
            {
                Id = new Guid("99999999-9999-9999-9999-999999999999"),
                Status = TabStatus.FileBacked,
                Text = "source code of a real file",
            },
        ]);

        Assert.Equal(2, plan.UsableNotes);
        var group = Assert.Single(plan.Groups);
        Assert.DoesNotContain(new Guid("99999999-9999-9999-9999-999999999999"), group.Sources);
        Assert.NotEqual(new Guid("99999999-9999-9999-9999-999999999999"), group.Container);
    }

    /// <summary>
    /// A compound theme must not be swallowed by its parent. "#project-mail" is
    /// a deliberate, separate theme even though the word "project" appears
    /// inside it.
    /// </summary>
    [Fact]
    public void Plan_KeepsCompoundThemeSeparateFromItsParent()
    {
        var plan = TidyPlanner.Plan(
        [
            Note("#project one", 0, "11111111-1111-1111-1111-111111111111"),
            Note("#project two", 1, "22222222-2222-2222-2222-222222222222"),
            Note("#project-mail\r\n\r\nlong list of addresses", 2, "33333333-3333-3333-3333-333333333333"),
        ]);

        var group = Assert.Single(plan.Groups);
        Assert.Equal("project", group.Theme);
        Assert.DoesNotContain(new Guid("33333333-3333-3333-3333-333333333333"), group.Sources);
    }

    [Fact]
    public void Plan_IgnoresThemesWithASingleNote()
    {
        var plan = TidyPlanner.Plan([Note("#alone by itself", 0, "11111111-1111-1111-1111-111111111111")]);

        Assert.Empty(plan.Groups);
        Assert.False(plan.HasWork);
    }
}
