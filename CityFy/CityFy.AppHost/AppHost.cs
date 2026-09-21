var builder = DistributedApplication.CreateBuilder(args);

var retriveDumpSpotify = builder.AddProject<Projects.CityFy_Retrieve_Dump_Spotify>("RetriveDumpSpotify")
    .WithHttpHealthCheck("/health");

var retriveSpotify = builder.AddProject<Projects.CityFy_Retrieve_Spotify>("RetrieveSpotify")
    .WithHttpHealthCheck("/health");

var retrieveMusicBrainz = builder.AddProject<Projects.CityFy_Retrieve_MusicBrainz>("RetrieveMusicBrainz")
    .WithHttpHealthCheck("/health");

var elaboration = builder.AddProject<Projects.CityFy_Elaboration>("Elaboration")
    .WithHttpHealthCheck("/health");

//builder.AddProject<Projects.CityFy_AppStarter_Web>("webfrontend")
//    .WithExternalHttpEndpoints()
//    .WithHttpHealthCheck("/health")
//    .WithReference(apiService)
//    .WaitFor(apiService);

builder.Build().Run();
