using Canhoes.Api.Access;
using Canhoes.Api.Data;
using Canhoes.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Canhoes.Api.Controllers;

[ApiController]
[Route("api/canhoes")]
[Authorize]
public partial class CanhoesController : ControllerBase
{
    private readonly CanhoesDbContext _db;
    private readonly IWebHostEnvironment _env;

    private sealed record ActiveEventAccessContext(
        string EventId,
        Guid UserId,
        bool IsAdmin,
        bool IsMember,
        EventModuleAccessSnapshot ModuleAccess)
    {
        public bool CanAccess => IsAdmin || IsMember;
        public bool CanManage => IsAdmin;
    }

    public sealed record AdminProposalsHistoryDto(
        ProposalsByStatus<CategoryProposalDto> CategoryProposals,
        ProposalsByStatus<MeasureProposalDto> MeasureProposals);

    public sealed record ProposalsByStatus<T>(IEnumerable<T> Pending, IEnumerable<T> Approved, IEnumerable<T> Rejected);

    public CanhoesController(CanhoesDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }
}
