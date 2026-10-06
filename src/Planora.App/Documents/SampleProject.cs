using Planora.Engine.Model;

namespace Planora.App.Documents;

/// <summary>A small but realistic project used for demos and UI screenshots.</summary>
public static class SampleProject
{
    public static Project Create(DateTime start)
    {
        const int Day = 480;
        var p = new Project("Office Relocation", start);
        p.Calendar.AddHoliday("Public holiday", DateOnly.FromDateTime(start.AddDays(17)));

        var pm = p.AddResource("Project Manager");
        pm.StandardRate = 60;
        var movers = p.AddResource("Movers");
        movers.StandardRate = 35;
        movers.MaxUnits = 3;
        var it = p.AddResource("IT Technician");
        it.StandardRate = 45;
        var crates = p.AddResource("Crates", ResourceType.Material);
        crates.MaterialLabel = "crates";
        crates.StandardRate = 4;

        ProjectTask Task(string name, int days, int level = 2)
        {
            var t = p.AddTask(name, days * Day);
            while (t.OutlineLevel < level)
                p.Indent(t);
            return t;
        }

        var planning = Task("Planning", 0, 1);
        var requirements = Task("Gather space requirements", 3);
        var site = Task("Select new site", 5);
        var lease = Task("Sign lease", 1);
        var planned = Task("Plan approved", 0);
        planned.IsMilestone = true;

        var preparation = Task("Preparation", 0, 1);
        var layout = Task("Design office layout", 4);
        var network = Task("Install network cabling", 6);
        var furniture = Task("Order furniture", 2);
        var delivery = Task("Furniture delivery", 8);

        var move = Task("Move", 0, 1);
        var pack = Task("Pack equipment", 3);
        var transport = Task("Transport to new office", 2);
        var setup = Task("Set up workstations", 3);
        var done = Task("Office open", 0);
        done.IsMilestone = true;
        done.Deadline = start.AddDays(45);

        p.Link(requirements, site);
        p.Link(site, lease);
        p.Link(lease, planned);
        p.Link(planned, layout);
        p.Link(layout, network);
        p.Link(layout, furniture);
        p.Link(furniture, delivery);
        p.Link(network, pack, LinkType.FinishToStart, -Day);
        p.Link(pack, transport);
        p.Link(delivery, transport);
        p.Link(transport, setup);
        p.Link(setup, done);

        p.Assign(requirements, pm);
        p.Assign(site, pm, 0.5);
        p.Assign(lease, pm);
        p.Assign(layout, pm);
        p.Assign(network, it);
        p.Assign(pack, movers, 2);
        p.Assign(pack, crates, 40);
        p.Assign(transport, movers, 3);
        p.Assign(setup, it);
        lease.FixedCost = 2500;
        delivery.FixedCost = 12000;

        requirements.ActualStart = start;
        requirements.PercentComplete = 100;
        site.PercentComplete = 60;
        site.ActualStart = start.AddDays(3);
        site.Notes = "Shortlist of three buildings.";
        return p;
    }
}
