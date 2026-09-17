using Lyubishchev_Time_Management.Data;
using Lyubishchev_Time_Management.Infrastructure.Clock;
using Lyubishchev_Time_Management.Infrastructure.Text;
using Lyubishchev_Time_Management.Models.Entities;
using Lyubishchev_Time_Management.Models.Requests;
using Lyubishchev_Time_Management.Models.Responses;
using Microsoft.EntityFrameworkCore;

namespace Lyubishchev_Time_Management.Services;

public sealed record CategoryResult(bool Succeeded, CategoryResponse? Category, string? ErrorCode, string? ErrorMessage)
{
    public static CategoryResult Ok(CategoryResponse category) => new(true, category, null, null);

    public static CategoryResult Fail(string errorCode, string errorMessage) => new(false, null, errorCode, errorMessage);
}

public sealed record CategoryDeleteResult(bool Succeeded, string? ErrorCode, string? ErrorMessage)
{
    public static CategoryDeleteResult Ok() => new(true, null, null);

    public static CategoryDeleteResult Fail(string errorCode, string errorMessage) => new(false, errorCode, errorMessage);
}

public sealed class CategoryService(AppDbContext dbContext, IClock clock)
{
    private const string NotFoundErrorMessage = "找不到指定的分類。";
    private const string ConflictErrorMessage = "已經有同名的分類。";
    private const string InvalidNameErrorMessage = "分類名稱不可為空白。";

    public async Task<IReadOnlyList<CategoryResponse>> ListAsync(ulong userId, CancellationToken cancellationToken)
        => await dbContext.Categories
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.NormalizedName)
            .Select(c => new CategoryResponse(c.Id, c.Name, c.Color))
            .ToListAsync(cancellationToken);

    public async Task<CategoryResult> CreateAsync(ulong userId, CategoryRequest request, CancellationToken cancellationToken)
    {
        if (!ResourceName.TryNormalize(request.Name, out var name, out var normalizedName))
        {
            return CategoryResult.Fail("INVALID_NAME", InvalidNameErrorMessage);
        }

        if (await NameConflictsAsync(userId, normalizedName, excludingId: null, cancellationToken))
        {
            return CategoryResult.Fail("CATEGORY_NAME_CONFLICT", ConflictErrorMessage);
        }

        var now = clock.UtcNow;
        var category = new Category
        {
            UserId = userId,
            Name = name,
            NormalizedName = normalizedName,
            Color = request.Color!,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            User = null!,
        };
        dbContext.Categories.Add(category);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another request for the same user created this normalized name between our read
            // and write. Detach the failed insert and confirm it really is a name collision
            // before swallowing the exception.
            dbContext.Entry(category).State = EntityState.Detached;
            if (!await NameConflictsAsync(userId, normalizedName, excludingId: null, cancellationToken))
            {
                throw;
            }

            return CategoryResult.Fail("CATEGORY_NAME_CONFLICT", ConflictErrorMessage);
        }

        return CategoryResult.Ok(new CategoryResponse(category.Id, category.Name, category.Color));
    }

    public async Task<CategoryResult> UpdateAsync(ulong userId, ulong id, CategoryRequest request, CancellationToken cancellationToken)
    {
        var category = await dbContext.Categories.SingleOrDefaultAsync(c => c.Id == id && c.UserId == userId, cancellationToken);
        if (category is null)
        {
            return CategoryResult.Fail("CATEGORY_NOT_FOUND", NotFoundErrorMessage);
        }

        if (!ResourceName.TryNormalize(request.Name, out var name, out var normalizedName))
        {
            return CategoryResult.Fail("INVALID_NAME", InvalidNameErrorMessage);
        }

        if (normalizedName != category.NormalizedName &&
            await NameConflictsAsync(userId, normalizedName, excludingId: id, cancellationToken))
        {
            return CategoryResult.Fail("CATEGORY_NAME_CONFLICT", ConflictErrorMessage);
        }

        category.Name = name;
        category.NormalizedName = normalizedName;
        category.Color = request.Color!;
        category.UpdatedAtUtc = clock.UtcNow;

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

            return CategoryResult.Fail("CATEGORY_NAME_CONFLICT", ConflictErrorMessage);
        }

        return CategoryResult.Ok(new CategoryResponse(category.Id, category.Name, category.Color));
    }

    public async Task<CategoryDeleteResult> DeleteAsync(ulong userId, ulong id, CancellationToken cancellationToken)
    {
        var affected = await dbContext.Categories
            .Where(c => c.Id == id && c.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        return affected > 0
            ? CategoryDeleteResult.Ok()
            : CategoryDeleteResult.Fail("CATEGORY_NOT_FOUND", NotFoundErrorMessage);
    }

    private async Task<bool> NameConflictsAsync(ulong userId, string normalizedName, ulong? excludingId, CancellationToken cancellationToken)
        => await dbContext.Categories.AnyAsync(
            c => c.UserId == userId && c.NormalizedName == normalizedName && (excludingId == null || c.Id != excludingId.Value),
            cancellationToken);
}
