using System.Text.Json.Serialization;

namespace Expovice.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<RateUnit>))]
public enum RateUnit
{
    Visit,
    Hour,
    Walk,
}

[JsonConverter(typeof(JsonStringEnumConverter<RequestStatus>))]
public enum RequestStatus
{
    Pending,
    Accepted,
    Completed,
    Declined,
    Cancelled,
}

[JsonConverter(typeof(JsonStringEnumConverter<ProviderSort>))]
public enum ProviderSort
{
    Distance,
    Rating,
}

[JsonConverter(typeof(JsonStringEnumConverter<PhotoSlot>))]
public enum PhotoSlot
{
    Profile,
    Work1,
    Work2,
    Work3,
    IdFront,
}

[JsonConverter(typeof(JsonStringEnumConverter<IdDocumentType>))]
public enum IdDocumentType
{
    Aadhaar,
    Pan,
    DrivingLicence,
}

[JsonConverter(typeof(JsonStringEnumConverter<ExternalProvider>))]
public enum ExternalProvider
{
    Google,
    Apple,
}
