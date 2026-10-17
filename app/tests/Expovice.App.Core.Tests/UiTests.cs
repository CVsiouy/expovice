using Expovice.App.Core.Ui;
using Expovice.App.Core.ViewModels;
using Microsoft.Maui.Graphics;

namespace Expovice.App.Core.Tests;

public sealed class FormattingTests
{
    [Theory]
    [InlineData(0, "₹0")]
    [InlineData(350, "₹350")]
    [InlineData(1050, "₹1,050")]
    [InlineData(105000, "₹1,05,000")]
    public void Rupees_use_indian_grouping(int amount, string expected) => Assert.Equal(expected, Formatting.Rupees(amount));

    [Fact]
    public void DayRange_collapses_contiguous_runs()
    {
        Assert.Equal("Mon–Sat", Formatting.DayRange([DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday]));
        Assert.Equal("Mon, Wed", Formatting.DayRange([DayOfWeek.Wednesday, DayOfWeek.Monday]));
        Assert.Equal("Every day", Formatting.DayRange(Formatting.Week));
    }

    [Fact]
    public void When_matches_the_prototype_copy()
    {
        var today = new DateOnly(2026, 10, 17);
        Assert.Equal("Today · 9–11 AM", Formatting.When(today, today, "9–11 AM"));
        Assert.Equal("Tomorrow · 3–5 PM", Formatting.When(today.AddDays(1), today, "3–5 PM"));
        Assert.Equal("Mon 19 Oct · 1–3 PM", Formatting.When(today.AddDays(2), today, "1–3 PM"));
    }

    [Fact]
    public void Every_hour_choice_round_trips()
    {
        foreach (var h in Choices.Hours)
        {
            Assert.Equal(h, Formatting.Hour(Formatting.ParseHour(h)));
        }

        Assert.Equal("7:30 PM", Formatting.Hour(new TimeOnly(19, 30)));
    }

    [Theory]
    [InlineData("12th Main, Indiranagar, Bengaluru", "Indiranagar")]
    [InlineData("Domlur", "Domlur")]
    public void Area_is_the_locality(string line, string area) => Assert.Equal(area, Formatting.AreaFromLine(line));

    [Fact]
    public void Phone_display_groups_digits() => Assert.Equal("+91 98765 43210", Formatting.PhoneDisplay("+919876543210"));
}

public sealed class LucideIconTests
{
    public static TheoryData<string> Keys() => [.. LucideIcons.Paths.Keys];

    [Theory]
    [MemberData(nameof(Keys))]
    public void Every_icon_path_parses_inside_the_24px_viewbox(string key)
    {
        foreach (var data in LucideIcons.Get(key))
        {
            var path = PathBuilder.Build(data);
            Assert.True(path.Count > 0, $"{key}: '{data}' produced no geometry");
            var b = path.GetBoundsByFlattening();
            Assert.InRange(b.Left, -0.5, 24.5);
            Assert.InRange(b.Right, -0.5, 24.5);
            Assert.InRange(b.Top, -0.5, 24.5);
            Assert.InRange(b.Bottom, -0.5, 24.5);
        }
    }

    [Fact]
    public void Circle_shorthand_becomes_an_arc_path() =>
        Assert.Equal("M9 12a3 3 0 1 0 6 0a3 3 0 1 0 -6 0z", LucideIcons.ToPath("c:12,12,3"));

    [Fact]
    public void Every_category_icon_exists()
    {
        foreach (var c in Contracts.Catalog.Categories)
        {
            Assert.NotEmpty(LucideIcons.Get(c.Icon));
        }
    }
}

public sealed class ChipTests
{
    [Fact]
    public void SelectOnly_is_radio_behaviour()
    {
        var items = new List<SelectableItem<int>>();
        items.AddRange(Enumerable.Range(0, 3).Select(i => new SelectableItem<int>(i, i.ToString(), p => Chips.SelectOnly(items, p), i == 0)));

        items[2].SelectCommand.Execute(null);

        Assert.Equal([2], Chips.Selected(items));
    }

    [Fact]
    public void Home_filter_matches_names_and_skills()
    {
        Assert.Equal(["plumber"], HomeViewModel.Filter("leak").Select(c => c.Id));
        Assert.Equal(["ac"], HomeViewModel.Filter("ac r").Select(c => c.Id));
        Assert.Empty(HomeViewModel.Filter("xyz"));
        Assert.Equal(8, HomeViewModel.Filter("  ").Count());
    }
}
