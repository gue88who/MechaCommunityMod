using GameRiver;
using GameRiver.Fight;
using Il2CppInterop.Runtime.InteropTypes;

namespace MechaCommunityMod.Plugins.BattleStatistics;

internal static class DamageSourceReader
{
    internal static DamageSource Read(IDamageProvider? provider)
    {
        if (provider is null) return DamageSource.Unknown;
        if (provider.TryCast<CommanderSkillDamageProvider>() is { } spell)
            return Effect(spell.commanderSkill);
        if (provider.TryCast<SkillDamageProvider>() is { } weapon)
            return Skill(weapon.fightSkill);
        if (provider.TryCast<FightProjectile>() is { } projectile)
        {
            var data = projectile.dataSource;
            if (data?.TryCast<FightSkill>() is { } skill) return Skill(skill);
            if (data?.TryCast<FightLandMine>() is { } mine) return Effect(mine.landMineContraption);
            return Effect(data);
        }
        if (provider.TryCast<DeadExplosiveDamageProvider>() is { } explosion)
        {
            if (explosion.deadExplosive?.TryCast<SkillData>() is not null) return DamageSource.Attack;
            return Effect(explosion.deadExplosive);
        }
        if (provider.TryCast<KillExplosionEffectProvider.KillExplosionDamageProvider>() is { } kill)
            return Effect(kill.dataSource);
        if (provider.TryCast<HitEffectControl>() is { } hit) return Effect(hit.dataSource);
        if (provider.TryCast<SupportUnitDamageProvider>() is not null)
            return new DamageSource(DamageCategory.Other, -10, "Air-drop impact");
        // NormalDamageProvider is also used by effects: its name is not proof of a normal shot.
        return DamageSource.Unknown;
    }

    private static DamageSource Skill(FightSkill? skill)
    {
        if (skill is null) return DamageSource.Unknown;
        var extra = skill.GetExtraSkillSource();
        if (extra is not null) return Effect(extra);
        return DamageSource.Attack;
    }

    internal static DamageSource Effect(Il2CppObjectBase? source)
    {
        if (source?.TryCast<Technology>() is { } tech)
        {
            var data = tech.GetTechnologyData();
            return EffectCatalog.Source(DamageCategory.Tech, data?.GetID() ?? source.TryCast<IEffectProviderDataSource>()?.GetID() ?? 0,
                data?.GetName() ?? "Technology effect");
        }
        if (source?.TryCast<CommanderSkillBase>() is { } spell)
            return EffectCatalog.Source(DamageCategory.Spell, spell.data.GetID(), spell.data.GetName());
        if (source?.TryCast<CommanderSkillData>() is { } spellData)
            return EffectCatalog.Source(DamageCategory.Spell, spellData.GetID(), spellData.GetName());
        if (source?.TryCast<TechnologyData>() is { } techData)
            return EffectCatalog.Source(DamageCategory.Tech, techData.GetID(), techData.GetName());
        if (source?.TryCast<Equipment>() is { } equipment)
            return new DamageSource(DamageCategory.Other, equipment.data.GetID(), equipment.data.GetName());
        if (source?.TryCast<LandMineContraption>() is { } mine)
            return new DamageSource(DamageCategory.Other, mine.data.GetID(), mine.GetName());
        if (source?.TryCast<Buff>() is { } buff) return EffectOrigins.Source(buff);
        if (source?.TryCast<FightSkill>() is { } skill) return Skill(skill);
        if (source?.TryCast<BuffData>() is { } buffData)
            return new DamageSource(DamageCategory.Other, buffData.GetID(), buffData.GetName());
        if (source?.TryCast<FireMech>() is not null)
            return new DamageSource(DamageCategory.Other, -11, "Spread ground fire");
        if (source?.TryCast<IEffectProviderDataSource>() is { } effect && effect.IsTechnologyEffect())
            return EffectCatalog.Source(DamageCategory.Tech, effect.GetID(), "Technology " + effect.GetID());
        return DamageSource.Unknown;
    }
}
