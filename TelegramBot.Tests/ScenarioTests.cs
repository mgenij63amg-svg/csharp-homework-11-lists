using Core.Entities;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramBot.Dto;
using TelegramBot.Scenarios;
using Xunit;
namespace TelegramBot.Tests;

public sealed class ScenarioTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task TaskCreatedOnlyAfterListChoice(bool withList)
    {
        using var f = new BotFixture();
        var list = withList ? await f.CreateList() : null;
        await f.Begin();
        Assert.Equal("Name", (await f.Context())!.CurrentStep);
        Assert.Equal(new[] { "/cancel" }, f.LastButtons);
        await f.Send("  Work  "); await f.Send("25.05.2025");
        Assert.Equal("List", (await f.Context())!.CurrentStep);
        Assert.Empty(await f.Items());
        Assert.Contains("tasklist|", f.LastInline);
        await f.Choose(list?.Id);
        var item = Assert.Single(await f.Items());
        Assert.Equal("Work", item.Name); Assert.Equal(new DateTime(2025,5,25), item.Deadline);
        Assert.Equal(list?.Id, item.List?.Id); Assert.Null(await f.Context());
        Assert.Equal(new[] { "/addtask", "/show", "/report" }, f.LastButtons);
    }

    [Theory]
    [InlineData("31.02.2027")] [InlineData("25.05.20254")] [InlineData("2027-02-28")]
    [InlineData("1.02.2027")] [InlineData("29.02.2025")] [InlineData("28.02.2027 ")]
    [InlineData(null)]
    public async Task InvalidDeadlineRetainsNameAndAcceptsCorrection(string? bad)
    {
        using var f = new BotFixture(); await f.Begin(); await f.Send("Name"); await f.Send(bad);
        Assert.Equal("Deadline", (await f.Context())!.CurrentStep);
        Assert.Equal("Name", (await f.Context())!.Data["Name"]); Assert.Empty(await f.Items());
        Assert.Contains("Неверный формат", f.LastText);
        await f.Send("29.02.2024"); await f.Choose(null);
        Assert.Equal(new DateTime(2024,2,29), Assert.Single(await f.Items()).Deadline);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task CancelAtEveryTaskStepDoesNotSave(int step)
    {
        using var f = new BotFixture(); await f.Begin();
        if (step >= 1) await f.Send("Abandoned");
        if (step >= 2) await f.Send("01.01.2030");
        await f.Send("/cancel"); Assert.Null(await f.Context()); Assert.Empty(await f.Items());
        Assert.Equal(new[] { "/addtask", "/show", "/report" }, f.LastButtons);
        await f.Send("/addtask"); Assert.Equal("Name", (await f.Context())!.CurrentStep);
        Assert.False((await f.Context())!.Data.ContainsKey("Name"));
    }

    [Theory]
    [InlineData("")] [InlineData(" ")] [InlineData(null)] [InlineData("/show")]
    public async Task InvalidTaskNameKeepsNameStep(string? name)
    {
        using var f = new BotFixture(); await f.Begin(); await f.Send(name);
        Assert.Equal("Name", (await f.Context())!.CurrentStep); Assert.Empty(await f.Items());
    }

    [Fact]
    public async Task TaskNameBoundaryIsChecked()
    {
        using var f = new BotFixture(); await f.Begin(); await f.Send(new string('Я',129));
        Assert.Equal("Name", (await f.Context())!.CurrentStep);
        await f.Send(new string('Я',128)); await f.Send("01.01.2030"); await f.Choose(null);
        Assert.Equal(128, Assert.Single(await f.Items()).Name.Length);
    }

    [Fact]
    public async Task ShowMenuUsesRequiredCallbackPayloadsAndThreeMainButtons()
    {
        using var f = new BotFixture(); var list = await f.CreateList(); await f.Send("/show");
        Assert.Equal("Выберите список", f.LastText);
        Assert.Equal(new[] { "show|", "show|"+list.Id, "addlist", "deletelist" }, f.LastInline);
        Assert.All(f.LastInline, x => Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(x),1,64));
        await f.Send("/report"); Assert.Equal(new[] { "/addtask", "/show", "/report" }, f.LastButtons);
    }

    [Fact]
    public async Task ShowFiltersByOwnerListAndActiveState()
    {
        using var f = new BotFixture(); var list = await f.CreateList();
        await f.CreateTask("InList",list.Id); await f.CreateTask("NoList"); await f.CreateTask("Foreign",user:2);
        await f.Send("/show"); await f.Click("show|"+list.Id);
        Assert.Contains("InList",f.LastText); Assert.DoesNotContain("NoList",f.LastText);
        await f.Click("show|"); Assert.Contains("NoList",f.LastText); Assert.DoesNotContain("Foreign",f.LastText);
        var item=(await f.Items()).Single(x=>x.Name=="InList");
        await f.Send("/completetask "+item.Id); await f.Click("show|"+list.Id);
        Assert.Equal("Активных задач нет.",f.LastText); await f.Send("/report"); Assert.Contains("Завершённых: 1",f.LastText);
    }

    [Fact]
    public async Task ListCreationRejectsBadAndDuplicateNamesWithoutLosingScenario()
    {
        using var f = new BotFixture(); await f.CreateList("Work"); await f.Send("/show"); await f.Click("addlist");
        foreach(var name in new[] {" ","12345678901"," work ","/show"})
        { await f.Send(name); Assert.Equal("Name",(await f.Context())!.CurrentStep); }
        await f.Send("1234567890"); Assert.Null(await f.Context());
        Assert.Equal(2,(await f.Lists.GetUserLists((await f.Users.GetUser(1,default))!.UserId,default)).Count);
    }

    [Fact]
    public async Task AddListCanBeCancelledAndCallbackCannotActAsName()
    {
        using var f=new BotFixture(); await f.Send("/start"); await f.Send("/show"); await f.Click("addlist");
        await f.Click("deletelist"); Assert.Equal(ScenarioType.AddList,(await f.Context())!.CurrentScenario);
        await f.Send("/cancel"); Assert.Null(await f.Context());
        Assert.Empty(await f.Lists.GetUserLists((await f.Users.GetUser(1,default))!.UserId,default));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task DeleteNeedsConfirmationAndAffectsOnlySelectedList(bool yes)
    {
        using var f=new BotFixture(); var list=await f.CreateList(); var keep=await f.CreateList("Keep");
        await f.CreateTask("DeleteActive",list.Id); await f.CreateTask("DeleteDone",list.Id);
        var done=(await f.Items()).Single(x=>x.Name=="DeleteDone"); await f.Send("/completetask "+done.Id);
        await f.CreateTask("KeepList",keep.Id); await f.CreateTask("NoList");
        await f.Send("/show"); await f.Click("deletelist"); Assert.Equal("Approve",(await f.Context())!.CurrentStep);
        await f.Click("deletelist|"+list.Id); Assert.Equal("Delete",(await f.Context())!.CurrentStep);
        Assert.Equal(new[]{"yes","no"},f.LastInline); Assert.NotNull(await f.Lists.Get(list.Id,default));
        await f.Click(yes?"yes":"no"); Assert.Null(await f.Context());
        Assert.Equal(yes?2:4,(await f.Items()).Count);
        Assert.Equal(yes,await f.Lists.Get(list.Id,default) is null);
        Assert.NotNull(await f.Lists.Get(keep.Id,default));
        Assert.Equal(new[]{"/addtask","/show","/report"},f.LastButtons);
        if(!yes)Assert.Contains("Удаление отменено",f.AllText);
    }

    [Fact]
    public async Task EmptyDeleteAndCancelAtConfirmationAreHarmless()
    {
        using var f=new BotFixture(); await f.Send("/start"); await f.Send("/show"); await f.Click("deletelist");
        Assert.Null(await f.Context()); Assert.Contains("нет списков",f.AllText);
        var list=await f.CreateList(); await f.CreateTask(listId:list.Id); await f.Send("/show"); await f.Click("deletelist");
        await f.Click("deletelist|"+list.Id); await f.Send("/cancel");
        Assert.NotNull(await f.Lists.Get(list.Id,default)); Assert.Single(await f.Items());
    }

    [Fact]
    public async Task ForeignOrMissingListCannotBeShownSelectedOrDeleted()
    {
        using var f=new BotFixture(); var own=await f.CreateList(); var other=await f.CreateList("Private",2);
        foreach(var id in new[]{other.Id,Guid.NewGuid()})
        {
            await f.Click("show|"+id); Assert.Contains("недоступен",f.LastText);
            await f.Begin(); await f.Send("Task"); await f.Send("01.01.2030"); await f.Choose(id);
            Assert.Empty(await f.Items()); Assert.Equal("List",(await f.Context())!.CurrentStep); await f.Send("/cancel");
            await f.Send("/show"); await f.Click("deletelist"); await f.Click("deletelist|"+id);
            Assert.Equal("Approve",(await f.Context())!.CurrentStep); await f.Send("/cancel");
        }
        Assert.NotNull(await f.Lists.Get(own.Id,default)); Assert.NotNull(await f.Lists.Get(other.Id,default));
    }

    [Fact]
    public async Task StaleButtonsAndTypedYesCannotDelete()
    {
        using var f=new BotFixture(); var list=await f.CreateList(); await f.Send("/show"); await f.Click("deletelist");
        await f.Click("deletelist|"+list.Id); var old=(int)(await f.Context())!.Data["PromptId"];
        await f.Send("yes"); Assert.NotNull(await f.Lists.Get(list.Id,default));
        await f.Send("/cancel"); await f.Send("/show"); await f.Click("deletelist"); await f.Click("deletelist|"+list.Id);
        await f.Click("yes",messageId:old); Assert.Equal("Delete",(await f.Context())!.CurrentStep);
        Assert.NotNull(await f.Lists.Get(list.Id,default)); await f.Click("no");
        await f.Click("yes",messageId:old); Assert.NotNull(await f.Lists.Get(list.Id,default));
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("show|invalid")]
    public async Task BadCallbacksDoNotBreakActiveScenario(string? data)
    {
        using var f=new BotFixture(); await f.Begin(); await f.Send("Name"); await f.Send("01.01.2030");
        await f.Click(data); Assert.Equal("List",(await f.Context())!.CurrentStep); Assert.Empty(await f.Items());
        await f.Choose(null); Assert.Single(await f.Items());
    }

    [Fact]
    public async Task UnregisteredCallbacksAreNotDispatchedAndAreAcknowledged()
    {
        using var f=new BotFixture();
        await f.Click("addlist",messageId:100); Assert.Null(await f.Context()); Assert.Null(await f.Users.GetUser(1,default));
        Assert.Single(f.Transport.Calls); Assert.Equal("answerCallbackQuery",f.Transport.Calls[0].Method);
    }

    [Fact]
    public async Task DifferentUsersHaveIndependentScenarios()
    {
        using var f=new BotFixture(); await Task.WhenAll(f.Begin(1),f.Begin(2));
        await Task.WhenAll(f.Send("One",1),f.Send("Two",2));
        await f.Send("01.01.2030",1); await f.Choose(null,1); await f.Send("/cancel",2);
        Assert.Equal("One",Assert.Single(await f.Items(1)).Name); Assert.Empty(await f.Items(2));
    }

    [Fact]
    public async Task FailedTaskConfirmationDoesNotDuplicateSavedTask()
    {
        using var f=new BotFixture(); await f.Begin(); await f.Send("Name"); await f.Send("01.01.2030");
        f.Transport.FailNextSend=true;
        await Assert.ThrowsAnyAsync<Exception>(()=>f.Choose(null)); Assert.Single(await f.Items());
        await f.Choose(null); Assert.Single(await f.Items()); Assert.Null(await f.Context());
    }

    [Fact]
    public async Task OldCommandsAreRemovedAndHelpDescribesNewFlow()
    {
        using var f=new BotFixture(); await f.Send("/start");
        foreach(var cmd in new[]{"/showtasks","/showalltasks","/showtask"})
        { await f.Send(cmd); Assert.Contains("/help",f.LastText); Assert.Null(await f.Context()); }
        await f.Send("/help"); Assert.Contains("/show",f.LastText); Assert.Contains("10",f.LastText);
        Assert.Contains("подтверждения",f.LastText); Assert.DoesNotContain("/showtasks",f.LastText);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, "")]
    [InlineData(2, "tasklist|broken")]
    [InlineData(2, "too-long")]
    public async Task MalformedCallbackKeepsScenarioKeyboard(int step, string? payload)
    {
        using var f = new BotFixture();
        await f.Begin();
        if (step >= 1) await f.Send("Keep name");
        if (step >= 2) await f.Send("01.01.2030");
        var expectedStep = (await f.Context())!.CurrentStep;

        await f.Click(payload == "too-long" ? new string('a', 65) : payload);

        Assert.Equal(expectedStep, (await f.Context())!.CurrentStep);
        Assert.Equal(new[] { "/cancel" }, f.LastButtons);
        Assert.Empty(await f.Items());
        if (step == 2)
        {
            await f.Choose(null);
            Assert.Equal("Keep name", Assert.Single(await f.Items()).Name);
            Assert.Null(await f.Context());
        }
        else
        {
            await f.Send("/cancel");
            Assert.Null(await f.Context());
            Assert.Empty(await f.Items());
        }
        Assert.Equal(new[] { "/addtask", "/show", "/report" }, f.LastButtons);
    }

    [Fact]
    public async Task NonMessagesGroupsAndMissingScenarioAreHandled()
    {
        using var f=new BotFixture(); await f.Handler.HandleUpdateAsync(f.Bot,new Update{Id=1},default);
        await f.Handler.HandleUpdateAsync(f.Bot,BotFixture.MessageUpdate("/start",type:ChatType.Group),default);
        Assert.Empty(f.Transport.Calls); Assert.Throws<InvalidOperationException>(()=>f.Handler.GetScenario(ScenarioType.None));
    }
}
