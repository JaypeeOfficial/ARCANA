using System.Security.Claims;
using CloudinaryDotNet.Actions;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using RDF.Arcana.API.Common;
using RDF.Arcana.API.Common.Extension;
using RDF.Arcana.API.Common.Helpers;
using RDF.Arcana.API.Common.Pagination;
using RDF.Arcana.API.Data;

namespace RDF.Arcana.API.Features.Client.All;

[Route("api/Clients")]
[ApiController]
public class GetAllClients : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IValidator<GetAllClientsQuery> _validator;

    public GetAllClients(IMediator mediator, IValidator<GetAllClientsQuery> validator)
    {
        _mediator = mediator;
        _validator = validator;
    }

    [HttpGet("GetAllClients")]
    public async Task<IActionResult> GetAllDirectRegistrationClient(
        [FromQuery] GetAllClientsQuery query)
    {
        try
        {
            if (User.Identity is ClaimsIdentity identity
                && IdentityHelper.TryGetUserId(identity, out var userId))
            {
                query.AccessBy = userId;

                var roleClaim = identity.Claims.SingleOrDefault(c => c.Type == ClaimTypes.Role);

                if (roleClaim != null)
                {
                    query.RoleName = roleClaim.Value;
                }
            }

            var regularClient = await _mediator.Send(query);
            Response.AddPaginationHeader(
                regularClient.CurrentPage,
                regularClient.PageSize,
                regularClient.TotalCount,
                regularClient.TotalPages,
                regularClient.HasPreviousPage,
                regularClient.HasNextPage
            );

            var result = new
            {
                regularClient,
                regularClient.CurrentPage,
                regularClient.PageSize,
                regularClient.TotalCount,
                regularClient.TotalPages,
                regularClient.HasPreviousPage,
                regularClient.HasNextPage
            };

            var successResult = Result<object>.Success(result, "Data fetch successfully");

            return Ok(successResult);
        }
        catch (Exception e)
        {
            return BadRequest(e.Message);
        }
    }

    public class GetAllClientsQuery : UserParams, IRequest<PagedList<GetAllClientResult>>
    {
        public string Search { get; set; }
        public bool? Status { get; set; }
        public string RegistrationStatus { get; set; }
        public string StoreType { get; set; }
        public string Origin { get; set; }
        public int AccessBy { get; set; }
        public string RoleName { get; set; }
    }

    public class GetAllClientResult
    {
        public int Id { get; set; }
        public int? RequestId { get; set; }
        public string OwnersName { get; set; }
        public OwnersAddressCollection OwnersAddress { get; set; }
        public string PhoneNumber { get; set; }
        public string EmailAddress { get; set; }
        public DateOnly DateOfBirth { get; set; }
        public string TinNumber { get; set; }
        public string BusinessName { get; set; }
        public BusinessAddressCollection BusinessAddress { get; set; }
        public string StoreType { get; set; }
        public string AuthorizedRepresentative { get; set; }
        public string AuthorizedRepresentativePosition { get; set; }
        public int Cluster { get; set; }
        public bool Freezer { get; set; }
        public string TypeOfCustomer { get; set; }
        public bool? DirectDelivery { get; set; }
        public string BookingCoverage { get; set; }
        public string ModeOfPayment { get; set; }
        public ClientTerms Terms { get; set; }
        public FixedDiscounts FixedDiscount { get; set; }
        public bool? VariableDiscount { get; set; }
        public string Longitude { get; set; }
        public string Latitude { get; set; }
        public string RequestedBy { get; set; }
        public IEnumerable<Attachment> Attachments { get; set; }


        public class FixedDiscounts
        {
            public decimal? DiscountPercentage { get; set; }
        }

        public class Attachment
        {
            public int DocumentId { get; set; }
            public string DocumentLink { get; set; }
            public string DocumentType { get; set; }
        }

        public class BusinessAddressCollection
        {
            public string HouseNumber { get; set; }
            public string StreetName { get; set; }
            public string BarangayName { get; set; }
            public string City { get; set; }
            public string Province { get; set; }
        }

        public class OwnersAddressCollection
        {
            public string HouseNumber { get; set; }
            public string StreetName { get; set; }
            public string BarangayName { get; set; }
            public string City { get; set; }
            public string Province { get; set; }
        }

        public class ClientTerms
        {
            public int TermId { get; set; }
            public string Term { get; set; }
            public int? CreditLimit { get; set; }
            public int? TermDays { get; set; }
            public int? TermDaysId { get; set; }
        }
    }

    public class Handler : IRequestHandler<GetAllClientsQuery, PagedList<GetAllClientResult>>
    {
        private const string REGULAR = "Regular";
        private const string APPROVED = "Approved";
        private const string UNDER_REVIEW = "Under review";
        private const string REJECTED = "Rejected";
        private const string APPROVER = "Approver";
        private readonly ArcanaDbContext _context;

        public Handler(ArcanaDbContext context)
        {
            _context = context;
        }

        public async Task<PagedList<GetAllClientResult>> Handle(GetAllClientsQuery request,
            CancellationToken cancellationToken)
        {
            var regularClients = _context.Clients.AsNoTracking();

            if (!string.IsNullOrEmpty(request.Search))
            {
                regularClients = regularClients.Where(x =>
                    x.BusinessName.Contains(request.Search) ||
                    x.StoreType.StoreTypeName.Contains(request.Search) ||
                    x.Fullname.Contains(request.Search)
                );
            }

            regularClients = request.RoleName switch
            {
                Roles.Approver when !string.IsNullOrWhiteSpace(request.RegistrationStatus) &&
                                    request.RegistrationStatus.ToLower() != Status.UnderReview.ToLower() =>
                    regularClients.Where(clients => clients.Request.Approvals.Any(x =>
                        x.Status == request.RegistrationStatus && x.ApproverId == request.AccessBy)),
                Roles.Approver => regularClients.Where(clients =>
                    clients.Request.Status == request.RegistrationStatus &&
                    clients.Request.CurrentApproverId == request.AccessBy),
                Roles.Admin or Roles.Cdo => regularClients.Where(x => 
                    x.AddedBy == request.AccessBy && x.RegistrationStatus == request.RegistrationStatus),
                _ => regularClients
            };

            if (request.RoleName is Roles.Approver && request.RegistrationStatus == Status.UnderReview)
            {
                regularClients = regularClients.Where(x => x.Request.CurrentApproverId == request.AccessBy);
            }
            if (request.Origin != null)
            {
                regularClients = regularClients.Where(x => x.Origin == request.Origin);
            }

            if (!string.IsNullOrEmpty(request.StoreType))
            {
                regularClients = regularClients.Where(x => x.StoreType.StoreTypeName == request.StoreType);
            }

            if (request.Status != null)
            {
                regularClients = regularClients.Where(x => x.IsActive == request.Status);
            }

            var result = regularClients.Select(client => new GetAllClientResult
            {
                Id = client.Id,
                RequestId = client.RequestId,
                OwnersName = client.Fullname,
                OwnersAddress = client.OwnersAddress != null
                    ? new GetAllClientResult.OwnersAddressCollection
                    {
                        HouseNumber = client.OwnersAddress.HouseNumber,
                        StreetName = client.OwnersAddress.StreetName,
                        BarangayName = client.OwnersAddress.Barangay,
                        City = client.OwnersAddress.City,
                        Province = client.OwnersAddress.Province
                    }
                    : null,
                PhoneNumber = client.PhoneNumber,
                EmailAddress = client.EmailAddress,
                DateOfBirth = client.DateOfBirthDB,
                TinNumber = client.TinNumber,
                BusinessName = client.BusinessName,
                BusinessAddress = client.BusinessAddress != null
                    ? new GetAllClientResult.BusinessAddressCollection
                    {
                        HouseNumber = client.BusinessAddress.HouseNumber,
                        StreetName = client.BusinessAddress.StreetName,
                        BarangayName = client.BusinessAddress.Barangay,
                        City = client.BusinessAddress.City,
                        Province = client.BusinessAddress.Province
                    }
                    : null,
                StoreType = client.StoreType.StoreTypeName,
                AuthorizedRepresentative = client.RepresentativeName,
                AuthorizedRepresentativePosition = client.RepresentativePosition,
                Cluster = client.Cluster,
                Freezer = client.Freezer,
                TypeOfCustomer = client.CustomerType,
                DirectDelivery = client.DirectDelivery,
                BookingCoverage = client.BookingCoverages.BookingCoverage,
                ModeOfPayment = client.ModeOfPayments.Payment,
                Terms = client.Term != null
                    ? new GetAllClientResult.ClientTerms
                    {
                        TermId = client.Term.TermsId,
                        Term = client.Term.Terms.TermType,
                        CreditLimit = client.Term.CreditLimit,
                        TermDaysId = client.Term.TermDaysId,
                        TermDays = client.Term.TermDays.Days
                    }
                    : null,
                FixedDiscount = client.FixedDiscounts != null
                    ? new GetAllClientResult.FixedDiscounts
                    {
                        DiscountPercentage = client.FixedDiscounts.DiscountPercentage
                    }
                    : null,
                VariableDiscount = client.VariableDiscount,
                Longitude = client.Longitude,
                Latitude = client.Latitude,
                RequestedBy = client.AddedByUser.Fullname,
                Attachments = client.ClientDocuments.Select(cd =>
                    new GetAllClientResult.Attachment
                    {
                        DocumentId = cd.Id,
                        DocumentLink = cd.DocumentPath,
                        DocumentType = cd.DocumentType
                    })
            });

            return await PagedList<GetAllClientResult>.CreateAsync(result, request.PageNumber, request.PageSize);
        }
    }
}