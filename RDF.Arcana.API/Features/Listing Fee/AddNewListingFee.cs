using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using RDF.Arcana.API.Common;
using RDF.Arcana.API.Common.Helpers;
using RDF.Arcana.API.Data;
using RDF.Arcana.API.Domain;
using RDF.Arcana.API.Features.Clients.Prospecting.Exception;

namespace RDF.Arcana.API.Features.Listing_Fee;

[Route("api/ListingFee"), ApiController]
public class AddNewListingFee : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IValidator<AddNewListingFeeCommand> _validator;

    public AddNewListingFee(IMediator mediator, IValidator<AddNewListingFeeCommand> validator)
    {
        _mediator = mediator;
        _validator = validator;
    }

    [HttpPost("AddNewListingFee")]
    public async Task<IActionResult> AddNewListingFeeRequest([FromBody] AddNewListingFeeCommand command)
    {
        var response = new QueryOrCommandResult<object>();
        try
        {
            var result = await _validator.ValidateAsync(command);

            if (!result.IsValid)
            {
                return BadRequest(result);
            }

            if (User.Identity is ClaimsIdentity identity
                && IdentityHelper.TryGetUserId(identity, out var userId))
            {
                command.RequestedBy = userId;
            }

            await _mediator.Send(command);
            response.Success = true;
            response.Status = StatusCodes.Status200OK;
            response.Messages.Add("Listing Fee requested successfully");
            return Ok(response);
        }
        catch (System.Exception e)
        {
            response.Messages.Add(e.Message);
            response.Status = StatusCodes.Status409Conflict;

            return Conflict(response);
        }
    }

    public class AddNewListingFeeCommand : IRequest<Unit>
    {
        public int ClientId { get; set; }
        public int RequestedBy { get; set; }
        public decimal Total { get; set; }
        public ICollection<ListingFeeItem> ListingItems { get; set; }

        public class ListingFeeItem
        {
            public int ItemId { get; set; }
            public int Sku { get; set; }
            public decimal UnitCost { get; set; }
        }
    }

    public class Handler : IRequestHandler<AddNewListingFeeCommand, Unit>
    {
        private const string FOR_APPROVAL = "For listing fee approval";
        private const string UNDER_REVIEW = "Under review";
        private readonly DataContext _context;

        public Handler(DataContext context)
        {
            _context = context;
        }

        public async Task<Unit> Handle(AddNewListingFeeCommand request, CancellationToken cancellationToken)
        {
            if (!await _context.Clients.AnyAsync(client => client.Id == request.ClientId, cancellationToken))
            {
                throw new ClientIsNotFound(request.ClientId);
            }

            var approval = new Approvals
            {
                ClientId = request.ClientId,
                ApprovalType = FOR_APPROVAL,
                IsActive = true,
                RequestedBy = request.RequestedBy,
            };

            await _context.Approvals.AddAsync(approval, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            var listingFee = new ListingFee
            {
                ClientId = request.ClientId,
                ApprovalsId = approval.Id,
                Status = UNDER_REVIEW,
                RequestedBy = request.RequestedBy,
                Total = request.Total
            };

            await _context.ListingFees.AddAsync(listingFee, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            foreach (var listingFeeItem in request.ListingItems.Select(items => new ListingFeeItems
                     {
                         ListingFeeId = listingFee.Id,
                         ItemId = items.ItemId,
                         Sku = items.Sku,
                         UnitCost = items.UnitCost
                     }))
            {
                await _context.ListingFeeItems.AddAsync(listingFeeItem, cancellationToken);
            }

            await _context.SaveChangesAsync(cancellationToken);

            return Unit.Value;
        }
    }
}