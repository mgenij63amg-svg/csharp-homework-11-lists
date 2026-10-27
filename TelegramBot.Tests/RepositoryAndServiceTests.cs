using Core.Entities;
using TelegramBot.Dto;
using TelegramBot.Repositories;
using TelegramBot.Scenarios;
using TelegramBot.Services;
using Xunit;
namespace TelegramBot.Tests;

public sealed class RepositoryAndServiceTests
{
    [Theory]
    [InlineData("show","show")] [InlineData("show|a|b","show")] [InlineData("addlist","addlist")]
    public void CallbackBaseUsesFirstPart(string input,string expected)
    { var dto=CallbackDto.FromString(input); Assert.Equal(expected,dto.Action); Assert.Equal(expected,dto.ToString()); }

    [Fact]
    public void ListCallbackRoundTripAndExtraParts()
    {
        var id=Guid.NewGuid(); var dto=ToDoListCallbackDto.FromString("show|"+id+"|extra");
        Assert.Equal(id,dto.ToDoListId); Assert.Equal("show|"+id,dto.ToString());
        Assert.Null(ToDoListCallbackDto.FromString("show").ToDoListId);
        Assert.Null(ToDoListCallbackDto.FromString("show|").ToDoListId);
        Assert.Equal("show|",new ToDoListCallbackDto{Action="show"}.ToString());
    }

    [Fact]
    public void CallbackBoundsAreMeasuredInUtf8Bytes()
    {
        Assert.Equal(new string('a',64),CallbackDto.FromString(new string('a',64)).ToString());
        Assert.Throws<FormatException>(()=>CallbackDto.FromString(new string('a',65)));
        Assert.Throws<FormatException>(()=>CallbackDto.FromString(new string('Я',33)));
        Assert.Throws<FormatException>(()=>ToDoListCallbackDto.FromString("show|broken"));
        Assert.Throws<FormatException>(()=>new ToDoListCallbackDto{Action=new string('a',40),ToDoListId=Guid.NewGuid()}.ToString());
    }

    [Fact]
    public async Task FileRepositoryCrudAndOwnerFilteringSurviveRecreation()
    {
        using var f=new BotFixture(); var one=new ToDoUser(); var two=new ToDoUser();
        var r=new FileToDoListRepository(f.DirectoryPath); var list=new ToDoList{Name="Work",User=one};
        await r.Add(list,default); await r.Add(new ToDoList{Name="Other",User=two},default);
        var again=new FileToDoListRepository(f.DirectoryPath);
        var saved=await again.Get(list.Id,default); Assert.Equal(list.CreatedAt,saved!.CreatedAt);
        Assert.Equal(one.UserId,saved.User.UserId); Assert.Single(await again.GetByUserId(one.UserId,default));
        Assert.True(await again.ExistsByName(one.UserId," work ",default));
        Assert.Null(await again.Get(Guid.NewGuid(),default)); await again.Delete(list.Id,default);
        Assert.Null(await r.Get(list.Id,default)); Assert.Single(await r.GetByUserId(two.UserId,default));
    }

    [Theory]
    [InlineData("")] [InlineData(" ")] [InlineData("12345678901")] [InlineData("/show")]
    public async Task ListServiceRejectsInvalidName(string name)
    { using var f=new BotFixture(); await Assert.ThrowsAsync<ArgumentException>(()=>f.Lists.Add(new ToDoUser(),name,default)); }

    [Fact]
    public async Task ListUniquenessIsPerUserAndAtomicAcrossRepositoryInstances()
    {
        using var f=new BotFixture(); var user=new ToDoUser(); var second=new ToDoUser();
        var services=Enumerable.Range(0,12).Select(_=>new ToDoListService(new FileToDoListRepository(f.DirectoryPath))).ToArray();
        var results=await Task.WhenAll(services.Select(async s=>
        { try { await s.Add(user,"Work",default); return true; } catch(ArgumentException){return false;} }));
        Assert.Single(results,x=>x); Assert.Single(await f.Lists.GetUserLists(user.UserId,default));
        await f.Lists.Add(second,"Work",default); Assert.Single(await f.Lists.GetUserLists(second.UserId,default));
        await f.Lists.Add(user,"1234567890",default);
    }

    [Fact]
    public async Task UsersListsAndTasksSurviveNewServiceInstances()
    {
        using var f=new BotFixture(); var list=await f.CreateList(); await f.CreateTask("Saved",list.Id);
        var users=new UserService(new FileUserRepository(f.DirectoryPath));
        var lists=new ToDoListService(new FileToDoListRepository(f.DirectoryPath)); var tasks=new ToDoService(f.DirectoryPath,lists);
        var original=await f.Users.GetUser(1,default); var restored=await users.RegisterUser(1,"changed",default);
        Assert.Equal(original!.UserId,restored.UserId); Assert.NotNull(await lists.Get(list.Id,default));
        var item=Assert.Single(await tasks.GetByUserIdAndList(restored.UserId,list.Id,default));
        Assert.Equal("Saved",item.Name); Assert.Equal(list.Id,item.List!.Id);
        Assert.Empty(await tasks.GetByUserIdAndList(restored.UserId,null,default));
    }

    [Fact]
    public async Task ServiceRejectsForeignListsAndOtherUsersCannotAlterTasks()
    {
        using var f=new BotFixture(); var list=await f.CreateList("Owner",2); await f.Send("/start",1);
        var user=(await f.Users.GetUser(1,default))!;
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Tasks.Add(user,"Bad",DateTime.Today,list,default));
        await f.CreateTask(); var item=Assert.Single(await f.Items());
        var other=(await f.Users.GetUser(2,default))!;
        Assert.False(await f.Tasks.Complete(other.UserId,item.Id,default)); Assert.False(await f.Tasks.Delete(other.UserId,item.Id,default));
        Assert.Equal(ToDoItemState.Active,Assert.Single(await f.Items()).State);
    }

    [Fact]
    public async Task CorruptJsonIsReportedAndNotOverwritten()
    {
        using var f=new BotFixture(); Directory.CreateDirectory(f.DirectoryPath);
        var path=Path.Combine(f.DirectoryPath,"lists.json"); await File.WriteAllTextAsync(path,"broken json");
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Lists.GetUserLists(Guid.NewGuid(),default));
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Lists.Add(new ToDoUser(),"Test",default));
        Assert.Equal("broken json",await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task ConcurrentContextsReplaceResetAndRejectMismatchedUser()
    {
        var repo=new InMemoryScenarioContextRepository();
        await Task.WhenAll(Enumerable.Range(1,100).Select(id=>repo.SetContext(id,new(ScenarioType.AddTask){UserId=id},default)));
        await repo.SetContext(1,new(ScenarioType.AddList){UserId=1},default);
        Assert.Equal(ScenarioType.AddList,(await repo.GetContext(1,default))!.CurrentScenario);
        await repo.ResetContext(1,default); Assert.Null(await repo.GetContext(1,default)); Assert.NotNull(await repo.GetContext(2,default));
        await Assert.ThrowsAsync<ArgumentException>(()=>repo.SetContext(1,new(ScenarioType.AddTask){UserId=2},default));
    }

    [Fact]
    public async Task CancelledTokensStopFileServicesContextsAndHandler()
    {
        using var f=new BotFixture(); var ct=new CancellationToken(true); var user=new ToDoUser();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.Users.RegisterUser(1,"X",ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.Lists.Add(user,"X",ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.Lists.GetUserLists(user.UserId,ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.Tasks.Add(user,"X",DateTime.Today,null,ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.Contexts.GetContext(1,ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.Send("/start",ct:ct)); Assert.Empty(f.Transport.Calls);
    }
}
