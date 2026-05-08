using Canhoes.Api.DTOs;
using Canhoes.Api.Models;

namespace Canhoes.Api.Services;

public interface IMemberService
{
    // Members
    Task<List<PublicUserDto>> GetMembersAsync(string eventId, CancellationToken ct);
    
    // Measures
    Task<List<GalaMeasureDto>> GetMeasuresAsync(string eventId, CancellationToken ct);
    Task<MeasureProposalDto> CreateMeasureProposalAsync(string eventId, Guid userId, CreateMeasureProposalRequest request, CancellationToken ct);
    
    // Results
    Task<List<CanhoesCategoryResultDto>> GetResultsAsync(string eventId, CancellationToken ct);
    
    // Nominations
    Task<MyNominationStatusDto> GetMyNominationStatusAsync(string eventId, Guid userId, CancellationToken ct);
    Task<List<NomineeDto>> GetApprovedNomineesAsync(string eventId, CancellationToken ct);
    Task<NomineeDto> CreateNominationAsync(string eventId, Guid userId, CreateNomineeRequest request, CancellationToken ct);
    Task<NomineeDto?> UpdateNomineeImageAsync(string eventId, string nomineeId, Guid userId, bool isAdmin, string imageUrl, CancellationToken ct);
    
    // Wishlist
    Task<EventWishlistItemDto?> UpdateWishlistImageAsync(string eventId, string itemId, Guid userId, bool isAdmin, string imageUrl, CancellationToken ct);
    Task<bool> DeleteWishlistItemAsync(string eventId, string itemId, Guid userId, bool isAdmin, CancellationToken ct);
}
