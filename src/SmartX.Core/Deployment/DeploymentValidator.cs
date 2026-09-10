namespace SmartX.Core.Deployment;

/// <summary>
/// Recursively validates a nested device-deployment tree (multi-tier configuration profile),
/// e.g. confirming that a node is safely configured within Sub-Zone B -&gt; Zone 1 -&gt;
/// Facility A. The tree can be arbitrarily deep in principle, which is exactly the case a
/// recursive algorithm - rather than a fixed set of nested loops - is suited to.
/// </summary>
public static class DeploymentValidator
{
    /// <summary>Tier names in the order they must appear walking down from the root.</summary>
    private static readonly string[] TierOrder = ["Facility", "Zone", "SubZone", "Node"];

    public static ValidationResult Validate(DeploymentNode root)
    {
        var errors = new List<string>();
        bool valid = ValidateRecursive(root, depth: 0, path: root.Name, errors);
        return new ValidationResult(valid, errors);
    }

    /// <summary>
    /// Recursive case: validate this node against its expected tier, then validate every
    /// child one tier deeper. Base case: a node with no children (a leaf) - recursion stops
    /// naturally because the foreach over an empty Children list does nothing further.
    /// </summary>
    private static bool ValidateRecursive(DeploymentNode node, int depth, string path, List<string> errors)
    {
        if (depth >= TierOrder.Length)
        {
            errors.Add($"{path}: nesting is deeper than the maximum supported tier (Node).");
            return false;
        }

        bool valid = true;

        if (node.Tier != TierOrder[depth])
        {
            errors.Add($"{path}: expected tier '{TierOrder[depth]}' at this depth but found '{node.Tier}'.");
            valid = false;
        }

        bool isLeafTier = TierOrder[depth] == "Node";

        if (isLeafTier)
        {
            if (node.Children.Count > 0)
            {
                errors.Add($"{path}: a Node tier must be a leaf - it cannot contain further children.");
                valid = false;
            }
            if (!node.HasSensorAssigned)
            {
                errors.Add($"{path}: leaf Node has no sensor assigned to it.");
                valid = false;
            }
            return valid;
        }

        if (node.Children.Count == 0)
        {
            errors.Add($"{path}: '{node.Tier}' tier must contain at least one child.");
            valid = false;
        }

        // Recursive step - walk every child one tier deeper.
        foreach (DeploymentNode child in node.Children)
        {
            string childPath = $"{path} -> {child.Name}";
            bool childValid = ValidateRecursive(child, depth + 1, childPath, errors);
            valid = valid && childValid;
        }

        return valid;
    }
}

public record ValidationResult(bool IsValid, List<string> Errors);
