using System;
using NUnit.Framework;
using Robust.Client.GameStates;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Serialization;
using System.Linq;
using System.Threading.Tasks;
using static Robust.UnitTesting.Shared.GameState.ExampleAutogeneratedComponent;

namespace Robust.UnitTesting.Shared.GameState;

/// <summary>
/// This is a test of test engine <see cref="RobustIntegrationTest"/>. Not a test of game engine.
/// </summary>
public sealed partial class NoSharedReferencesTest : RobustIntegrationTest
{
    /// <summary>
    /// The test performs a basic check to ensure that there is no issue with server's object references leaking to client.
    /// It accomplishments this by testing two things: 1) That the reference object on both sides is not the same; and
    /// 2) That the client-side copy of server's component state (used for prediction resetting) is not the same.
    /// </summary>
    [Test]
    public async Task ReferencesAreNotShared()
    {
        var serverOpts = new ServerIntegrationOptions { Pool = false };
        var clientOpts = new ClientIntegrationOptions { Pool = false };
        var server = StartServer(serverOpts);
        var client = StartClient(clientOpts);

        await Task.WhenAll(client.WaitIdleAsync(), server.WaitIdleAsync());
        var netMan = client.ResolveDependency<IClientNetManager>();
        var clientGameStateManager = client.ResolveDependency<IClientGameStateManager>();

        Assert.DoesNotThrow(() => client.SetConnectTarget(server));
        client.Post(() => netMan.ClientConnect(null!, 0, null!));

        // Set up map.
        server.Post(() =>
        {
            server.System<SharedMapSystem>().CreateMap();
        });

        await RunTicks();

        EntityUid sPlayer = default;
        EntityUid cPlayer = default;
        ExampleObject serverObject = default!;
        var sEntMan = server.EntMan;

        // Set up test entity (player).
        await server.WaitPost(() =>
        {
            sPlayer = sEntMan.Spawn();
            serverObject = new ExampleObject(5);

            var comp = new ExampleAutogeneratedComponent { ReferenceObject = serverObject };
            sEntMan.AddComponent(sPlayer, comp);

            var session = server.PlayerMan.Sessions.First();
            server.PlayerMan.SetAttachedEntity(session, sPlayer);
            server.PlayerMan.JoinGame(session);
        });

        // Let Client sync state with Server
        await RunTicks();

        // Assert that Client's object and client-side server state objects are different to Server's object
        Assert.Multiple(() =>
        {
            // Player attached assertions
            var cEntMan = client.EntMan;
            cPlayer = cEntMan.GetEntity(server.EntMan.GetNetEntity(sPlayer));
            Assert.That(client.AttachedEntity, Is.EqualTo(cPlayer));
            Assert.That(cEntMan.EntityExists(cPlayer));

            // Assert client and server have different objects of same values
            Assert.That(cEntMan.TryGetComponent(cPlayer, out ExampleAutogeneratedComponent? comp));
            var clientObject = comp?.ReferenceObject;
            Assert.That(clientObject, Is.EqualTo(serverObject));
            Assert.That(ReferenceEquals(clientObject, serverObject), Is.False);

            // Assert that client-side dictionary of server component state also isn't contaminated by server references
            var componentStates = clientGameStateManager.GetFullRep()[cEntMan.GetNetEntity(cPlayer)];
            var clientLastTickStateObject = ((ExampleAutogeneratedComponent_AutoState)componentStates.First(x => x.Value is ExampleAutogeneratedComponent_AutoState).Value!).ReferenceObject;
            Assert.That(clientLastTickStateObject, Is.Not.Null);
            Assert.That(ReferenceEquals(clientLastTickStateObject, serverObject), Is.False);
        });

        // wait for errors.
        await RunTicks();

        async Task RunTicks()
        {
            for (int i = 0; i < 10; i++)
            {
                await server.WaitRunTicks(1);
                await client.WaitRunTicks(1);
            }
        }

        await client.WaitPost(() => netMan.ClientDisconnect(""));
        await server.WaitRunTicks(5);
        await client.WaitRunTicks(5);
    }
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ExampleAutogeneratedComponent : Component
{
    [AutoNetworkedField]
    public ExampleObject ReferenceObject;

    [Serializable, NetSerializable]
    public sealed record ExampleObject(int Value);
}
