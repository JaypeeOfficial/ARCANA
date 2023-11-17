﻿using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using RDF.Arcana.API.Common;
using RDF.Arcana.API.Data;
using RDF.Arcana.API.Domain;
using RDF.Arcana.API.Features.Clients.Prospecting.Exception;

namespace RDF.Arcana.API.Features.Client.Prospecting.Register;

[Route("api/Registration")]
[ApiController]
public class AddTermsAndCondition : ControllerBase
{
    private readonly IMediator _mediator;

    public AddTermsAndCondition(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPut("AddTermsAndCondition/{id}")]
    public async Task<IActionResult> AddTermsCondition([FromBody] AddTermsAndConditionsCommand command,
        [FromRoute] int id)
    {
        var response = new QueryOrCommandResult<object>();
        try
        {
            if (User.Identity is ClaimsIdentity identity
                && int.TryParse(identity.FindFirst("id")?.Value, out var userId))
            {
                command.AddedBy = userId;
            }

            command.ClientId = id;
            await _mediator.Send(command);
            response.Success = true;
            response.Status = StatusCodes.Status200OK;
            response.Messages.Add("Terms and Conditions added successfully");
            return Ok(response);
        }
        catch (Exception ex)
        {
            response.Messages.Add(ex.Message);
            response.Status = StatusCodes.Status404NotFound;
            return Conflict(response);
        }
    }

    public class AddTermsAndConditionsCommand : IRequest<Unit>
    {
        public int ClientId { get; set; }
        public bool Freezer { get; set; }
        public string TypeOfCustomer { get; set; }
        public bool DirectDelivery { get; set; }
        public int BookingCoverageId { get; set; }
        public int ModeOfPayment { get; set; }
        public int Terms { get; set; }
        public int? CreditLimit { get; set; }
        public int? TermDaysId { get; set; }
        public Dicount FixedDiscount { get; set; }
        public bool VariableDiscount { get; set; }
        public int AddedBy { get; set; }

        public class Dicount
        {
            public decimal? DiscountPercentage { get; set; }
        }
    }

    public class Handler : IRequestHandler<AddTermsAndConditionsCommand, Unit>
    {
        private readonly ArcanaDbContext _context;

        public Handler(ArcanaDbContext context)
        {
            _context = context;
        }

        public async Task<Unit> Handle(AddTermsAndConditionsCommand request, CancellationToken cancellationToken)
        {
            var existingClient = await _context.Clients.FirstOrDefaultAsync(
                x => x.Id == request.ClientId, cancellationToken
            );

            if (existingClient == null) throw new ClientIsNotFound(request.ClientId);
            existingClient.CustomerType = request.TypeOfCustomer;
            existingClient.Freezer = request.Freezer;
            existingClient.DirectDelivery = request.DirectDelivery;
            existingClient.BookingCoverageId = request.BookingCoverageId;
            existingClient.ModeOfPayment = request.ModeOfPayment;

            var limit = request.CreditLimit;

            if (request.CreditLimit.HasValue)
            {
                limit = request.CreditLimit.Value;
            }

            var validateTerms = await _context.Terms.FirstOrDefaultAsync(x => x.Id == request.Terms, cancellationToken);

            if (validateTerms is null)
            {
                throw new Exception("Terms not found");
            }

            var termsOptions = new TermOptions
            {
                TermsId = request.Terms,
                CreditLimit = limit,
                TermDaysId = request.TermDaysId,
                AddedBy = request.AddedBy
            };

            await _context.TermOptions.AddAsync(termsOptions, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            existingClient.Terms = termsOptions.Id;
            //For validation
            // Check if the user can have no discount at all
            if (request.FixedDiscount?.DiscountPercentage != null)
            {
                var fixedDiscount = new FixedDiscounts
                {
                    ClientId = existingClient.Id,
                    DiscountPercentage = request.FixedDiscount.DiscountPercentage / 100
                };

                await _context.FixedDiscounts.AddAsync(fixedDiscount, cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);

                var discountId = fixedDiscount.Id;
                existingClient.FixedDiscountId = discountId;
            }
            else
            {
                existingClient.VariableDiscount = request.VariableDiscount;
                await _context.SaveChangesAsync(cancellationToken);
                return Unit.Value;
            }

            return Unit.Value;
        }
    }
}