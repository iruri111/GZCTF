﻿using CTFServer.Models.Request.Game;
using CTFServer.Repositories.Interface;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CTFServer.Repositories;

public class GameRepository : RepositoryBase, IGameRepository
{
    private readonly IMemoryCache cache;

    public GameRepository(IMemoryCache memoryCache, AppDbContext _context) : base(_context)
    {
        cache = memoryCache;
    }

    public async Task<Game?> CreateGame(Game game, CancellationToken token = default)
    {
        await context.AddAsync(game, token);
        await SaveAsync(token);
        return game;
    }

    public Task<Game?> GetGameById(int id, CancellationToken token = default)
        => context.Games.FirstOrDefaultAsync(x => x.Id == id, token);

    public async Task<BasicGameInfoModel[]> GetBasicGameInfo(int count = 10, int skip = 0, CancellationToken token = default)
        => (await cache.GetOrCreateAsync(CacheKey.BasicGameInfo, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(2);
            return context.Games.OrderByDescending(g => g.StartTimeUTC)
                .Select(g => BasicGameInfoModel.FromGame(g)).ToArrayAsync(token);
        })).Skip(skip).Take(count).ToArray();

    public Task<ScoreboardModel> GetScoreboard(Game game, CancellationToken token = default)
        => cache.GetOrCreateAsync(CacheKey.ScoreBoard(game.Id), entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12);
            return GenScoreboard(game, token);
        });

    public Task<Game[]> GetGames(int count, int skip, CancellationToken token)
        => context.Games.OrderByDescending(g => g.StartTimeUTC).Skip(skip).Take(count).ToArrayAsync(token);

    public void FlushGameInfoCache()
        => cache.Remove(CacheKey.BasicGameInfo);

    public void FlushScoreboard(int gameId)
        => cache.Remove(CacheKey.ScoreBoard(gameId));

    #region Generate Scoreboard

    private record Data(Instance Instance, Submission? Submission);

    // By xfoxfu & GZTimeWalker @ 2022/04/03
    private async Task<ScoreboardModel> GenScoreboard(Game game, CancellationToken token = default)
    {
        var data = await FetchData(game, token);
        var bloods = GenBloods(data);
        var items = GenScoreboardItems(data, bloods);
        return new()
        {
            Challenges = GenChallenges(data, bloods),
            Items = items,
            TimeLine = GenTopTimeLines(items)
        };
    }

    private Task<Data[]> FetchData(Game game, CancellationToken token = default)
        => context.Instances
            .AsNoTracking() // these data wont be changed with this query
            .Include(i => i.Challenge)
            .Where(i => i.Challenge.Game == game && i.Challenge.IsEnabled && i.Participation.Status == ParticipationStatus.Accepted)
            .Include(i => i.Participation)
            .GroupJoin(
                context.Submissions.Where(s => s.Status == AnswerResult.Accepted),
                i => new { i.ChallengeId, i.ParticipationId },
                s => new { s.ChallengeId, s.ParticipationId },
                (i, s) => new { Instance = i, Submissions = s }
            ).SelectMany(j => j.Submissions.DefaultIfEmpty(),
                (j, s) => new Data(j.Instance, s)
            ).ToArrayAsync(token);

    private static IDictionary<int, Blood?[]> GenBloods(Data[] data)
        => data.GroupBy(j => j.Instance.Challenge).Select(g => new
        {
            g.Key,
            Value = g.GroupBy(c => c.Submission?.TeamId)
                .Where(t => t.Key is not null)
                .Select(c =>
                {
                    var s = c.OrderBy(t => t.Submission?.SubmitTimeUTC ?? DateTimeOffset.UtcNow)
                        .FirstOrDefault(s => s.Submission?.Status == AnswerResult.Accepted);

                    return s is null ? null : new Blood
                    {
                        Id = s.Instance.Participation.TeamId,
                        Avatar = s.Instance.Participation.Team.AvatarUrl,
                        Name = s.Instance.Participation.Team.Name,
                        SubmitTimeUTC = s.Submission?.SubmitTimeUTC
                    };
                })
                .Where(t => t is not null)
                .OrderBy(t => t?.SubmitTimeUTC ?? DateTimeOffset.UtcNow)
                .Take(3).ToArray(),
        }).ToDictionary(a => a.Key.Id, a => a.Value);

    private static IDictionary<ChallengeTag, IEnumerable<ChallengeInfo>> GenChallenges(Data[] data, IDictionary<int, Blood?[]> bloods)
        => data.GroupBy(g => g.Instance.Challenge)
            .Select(c => new ChallengeInfo
            {
                Id = c.Key.Id,
                Title = c.Key.Title,
                Tag = c.Key.Tag,
                Score = c.Key.CurrentScore,
                SolvedCount = c.Key.AcceptedCount,
                Bloods = bloods[c.Key.Id]
            }).GroupBy(c => c.Tag)
            .ToDictionary(c => c.Key, c => c.AsEnumerable());

    private static IEnumerable<ScoreboardItem> GenScoreboardItems(Data[] data, IDictionary<int, Blood?[]> bloods)
        => data.GroupBy(j => j.Instance.Participation)
            .Select(j => new
            {
                Item = new ScoreboardItem
                {
                    Id = j.Key.Team.Id,
                    Name = j.Key.Team.Name,
                    Avatar = j.Key.Team.AvatarUrl,
                    Rank = 0,
                    SolvedCount = j.GroupBy(s => s.Instance.ChallengeId)
                        .Count(c => c.Any(s => s.Submission?.Status == AnswerResult.Accepted)),
                    Challenges = j.GroupBy(s => s.Instance.ChallengeId)
                        .Select(c =>
                        {
                            var cid = c.Key;
                            var s = c.OrderBy(s => s.Submission?.SubmitTimeUTC ?? DateTimeOffset.UtcNow)
                                .FirstOrDefault(s => s.Submission?.Status == AnswerResult.Accepted);

                            SubmissionType status = SubmissionType.Normal;

                            if (s?.Submission is null)
                                status = SubmissionType.Unaccepted;
                            else if (bloods[cid][0] is not null && s.Submission.SubmitTimeUTC <= bloods[cid][0]!.SubmitTimeUTC)
                                status = SubmissionType.FirstBlood;
                            else if (bloods[cid][1] is not null && s.Submission.SubmitTimeUTC <= bloods[cid][1]!.SubmitTimeUTC)
                                status = SubmissionType.SecondBlood;
                            else if (bloods[cid][2] is not null && s.Submission.SubmitTimeUTC <= bloods[cid][2]!.SubmitTimeUTC)
                                status = SubmissionType.ThirdBlood;

                            return new ChallengeItem
                            {
                                Id = cid,
                                Type = status,
                                SubmitTimeUTC = s?.Submission?.SubmitTimeUTC,
                                Score = s is null ? 0 : status switch
                                {
                                    SubmissionType.Unaccepted => 0,
                                    SubmissionType.FirstBlood => Convert.ToInt32(s.Instance.Challenge.CurrentScore * 1.05f),
                                    SubmissionType.SecondBlood => Convert.ToInt32(s.Instance.Challenge.CurrentScore * 1.03f),
                                    SubmissionType.ThirdBlood => Convert.ToInt32(s.Instance.Challenge.CurrentScore * 1.01f),
                                    SubmissionType.Normal => s.Instance.Challenge.CurrentScore,
                                    _ => throw new ArgumentException(nameof(status))
                                }
                            };
                        }).ToList()
                },
                LastSubmissionTime = j.Select(s => s.Submission?.SubmitTimeUTC ?? DateTimeOffset.UtcNow)
                    .OrderByDescending(t => t).FirstOrDefault()
            }).OrderByDescending(j => j.Item.Score) //成绩倒序，最后提交时间正序
            .ThenBy(j => j.LastSubmissionTime)
            .Select((j, i) =>
            {
                j.Item.Rank = i + 1;
                return j.Item;
            }).ToArray();

    private static IEnumerable<TopTimeLine> GenTopTimeLines(IEnumerable<ScoreboardItem> items)
        => items.Select(team => new TopTimeLine
        {
            Id = team.Id,
            Name = team.Name,
            Items = GenTimeLine(team.Challenges)
        }).ToArray();

    private static IEnumerable<TimeLine> GenTimeLine(IEnumerable<ChallengeItem> items)
    {
        var score = 0;
        List<TimeLine> timeline = new();
        foreach (var item in items.Where(i => i.SubmitTimeUTC is not null).OrderBy(i => i.SubmitTimeUTC))
        {
            score += item.Score;
            timeline.Add(new()
            {
                Score = score,
                Time = item.SubmitTimeUTC!.Value // 此处不为 null
            });
        }
        return timeline.ToArray();
    }

    #endregion Generate Scoreboard
}