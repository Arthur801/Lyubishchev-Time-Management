using Lyubishchev_Time_Management.Data;
using Lyubishchev_Time_Management.Infrastructure.Clock;
using Lyubishchev_Time_Management.Infrastructure.Text;
using Lyubishchev_Time_Management.Models.Entities;
using Lyubishchev_Time_Management.Models.Requests;
using Lyubishchev_Time_Management.Models.Responses;
using Microsoft.EntityFrameworkCore;

namespace Lyubishchev_Time_Management.Services;

public sealed record TagResult(bool Succeeded, TagResponse? Tag, string? ErrorCode, string? ErrorMessage)
{
    public static TagResult Ok(TagResponse tag) => new(true, tag, null, null);

    public static TagResult Fail(string errorCode, string errorMessage) => new(false, null, errorCode, errorMessage);
}

public sealed record TagDeleteResult(bool Succeeded, string? ErrorCode, string? ErrorMessage)
{
    public static TagDeleteResult Ok() => new(true, null, null);

    public static TagDeleteResult Fail(string errorCode, string errorMessage) => new(false, errorCode, errorMessage);
}

public sealed class TagService(AppDbContext dbContext, IClock clock)
{
    private const string NotFoundErrorMessage = "找不到指定的標籤。";
    private const string ConflictErrorMessage = "已經有同名的標籤。";
    private const string InvalidNameErrorMessage = "標籤名稱不可為空白。";

    public async Task<IReadOnlyList<TagResponse>> ListAsync(ulong userId, CancellationToken cancellationToken)
        => await dbContext.Tags
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.NormalizedName)
            .Select(t => new TagResponse(t.Id, t.Name))
            .ToListAsync(cancellationToken);

    public async Task<TagResult> CreateAsync(ulong userId, TagRequest request, CancellationToken cancellationToken)
    {
        if (!ResourceName.TryNormalize(request.Name, out var name, out var normalizedName))
        {
            return TagResult.Fail("INVALID_NAME", InvalidNameErrorMessage);
        }

        if (await NameConflictsAsync(userId, normalizedName, excludingId: null, cancellationToken))
        {
            return TagResult.Fail("TAG_NAME_CONFLICT", ConflictErrorMessage);
        }

        var now = clock.UtcNow;
        var tag = new Tag
        {
            UserId = userId,
            Name = name,
            NormalizedName = normalizedName,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            User = null!,
        };
        dbContext.Tags.Add(tag);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(tag).State = EntityState.Detached;
            if (!await NameConflictsAsync(userId, normalizedName, excludingId: null, cancellationToken))
            {
                throw;
            }

            return TagResult.Fail("TAG_NAME_CONFLICT", ConflictErrorMessage);
        }

        return TagResult.Ok(new TagResponse(tag.Id, tag.Name));
    }

    public async Task<TagResult> UpdateAsync(ulong userId, ulong id, TagRequest request, CancellationToken cancellationToken)
    {
        var tag = await dbContext.Tags.SingleOrDefaultAsync(t => t.Id == id && t.UserId == userId, cancellationToken);
        if (tag is null)
        {
            return TagResult.Fail("TAG_NOT_FOUND", NotFoundErrorMessage);
        }

        if (!ResourceName.TryNormalize(request.Name, out var name, out var normalizedName))
        {
            return TagResult.Fail("INVALID_NAME", InvalidNameErrorMessage);
        }

        if (normalizedName != tag.NormalizedName &&
            await NameConflictsAsync(userId, normalizedName, excludingId: id, cancellationToken))
        {
            return TagResult.Fail("TAG_NAME_CONFLICT", ConflictErrorMessage);
        }

        tag.Name = name;
        tag.NormalizedName = normalizedName;
        tag.UpdatedAtUtc = clock.UtcNow;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (!await NameConflictsAsync(userId, normalizedName, excludingId: id, cancellationToken))
            {
                throw;
            }

            return TagResult.Fail("TAG_NAME_CONFLICT", ConflictErrorMessage);
        }

        return TagResult.Ok(new TagResponse(tag.Id, tag.Name));
    }

    public async Task<TagDeleteResult> DeleteAsync(ulong userId, ulong id, CancellationToken cancellationToken)
    {
        var affected = await dbContext.Tags
            .Where(t => t.Id == id && t.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        return affected > 0
            ? TagDeleteResult.Ok()
            : TagDeleteResult.Fail("TAG_NOT_FOUND", NotFoundErrorMessage);
    }

    private async Task<bool> NameConflictsAsync(ulong userId, string normalizedName, ulong? excludingId, CancellationToken cancellationToken)
        => await dbContext.Tags.AnyAsync(
            t => t.UserId == userId && t.NormalizedName == normalizedName && (excludingId == null || t.Id != excludingId.Value),
            cancellationToken);
}
