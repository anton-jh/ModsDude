using ModsDude.Server.Domain.Mods;

namespace ModsDude.Server.Domain.Storage;

/// <summary>One container's plan, and the marker the container carries.</summary>
public record ContainerReclamation(string Container, ReclamationPlan Plan, Guid? Marker);

/// <param name="Refusals">Why nothing may be deleted, one line per container at fault. Empty when the sweep may go ahead.</param>
/// <param name="ToMark">The containers to mark once the binding exists, before anything is deleted.</param>
/// <param name="CreateBinding">Whether the database has no binding yet and must get one first.</param>
public record ReclamationDecision(IReadOnlyList<string> Refusals, IReadOnlyList<string> ToMark, bool CreateBinding)
{
    public bool MayProceed => Refusals.Count == 0;
}

/// <summary>
/// Whether a sweep may delete anything at all. Pure, like <see cref="BlobReclamation"/>, because it is
/// what stands between a database that is not storage's own and every blob in the account.
/// </summary>
/// <remarks>
/// <para>
/// A container marked with this database's binding is swept on its plan alone, however much of it
/// that reclaims - the residue of a deleted repo can be most of a container. A container marked with
/// another database's binding, or marked while this database has none, is never swept.
/// </para>
/// <para>
/// A container with no marker cannot say whose it is, so it gets the older, heuristic check: a plan
/// reclaiming more than <c>maxShare</c> of it is taken to mean the wrong database. Passing that, it is
/// marked and is bound from then on.
/// </para>
/// </remarks>
public static class ReclamationGate
{
    public static ReclamationDecision Decide(IReadOnlyList<ContainerReclamation> containers, Guid? databaseBinding, double maxShare)
    {
        var refusals = new List<string>();
        var toMark = new List<string>();

        foreach (var container in containers)
        {
            switch (StorageBinding.Judge(databaseBinding, container.Marker))
            {
                case ContainerBinding.Foreign:
                    refusals.Add(databaseBinding is null
                        ? $"'{container.Container}' is bound to a database, and this one has no binding."
                        : $"'{container.Container}' is bound to another database.");
                    break;

                case ContainerBinding.Unbound when container.Plan.IsImplausible(maxShare):
                    refusals.Add($"'{container.Container}' is not bound yet and would lose {container.Plan.Reclaimable.Count} of {container.Plan.Scanned} blobs, more than {maxShare:P0}.");
                    break;

                case ContainerBinding.Unbound:
                    toMark.Add(container.Container);
                    break;
            }
        }

        return refusals.Count > 0
            ? new ReclamationDecision(refusals, [], false)
            : new ReclamationDecision([], toMark, databaseBinding is null && toMark.Count > 0);
    }
}
