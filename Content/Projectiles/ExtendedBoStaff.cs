using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Audio;
using PreHardmodePlus.Content.Dusts;
using Terraria.GameContent;
using ReLogic.Content;

    namespace PreHardmodePlus.Content.Projectiles
    {
        // ExtendedBoStaff is an adaption of Example Advanced Flail
        // Example Advanced Flail is a complete adaption of Ball O' Hurt projectile.
        // Example Advanced Flail shows a plethora of advanced AI and collision topics.
        public class ExtendedBoStaff : ModProjectile
        {
            private enum AIState
            {
                Spinning,
                Hit,
                Return,
                ForcedReturn,
                Ricochet,
                Drop
            }

        // These properties wrap the usual AI and localAI arrays for cleaner and easier to understand code
        private AIState CurrAIState
        {
            get => (AIState)Projectile.ai[0];
            set => Projectile.ai[0] = (float)value;
        }
        public ref float StateTimer => ref Projectile.ai[1];
        public ref float CollisionCounter => ref Projectile.localAI[0];
        public ref float SpinningStateTimer => ref Projectile.localAI[1];

        public override void SetStaticDefaults()
        {
            DisplayName.SetDefault("Bo Staff");

            // These lines facilitate the trail drawing
            ProjectileID.Sets.TrailCacheLength[Projectile.type] = 6;
            ProjectileID.Sets.TrailingMode[Projectile.type] = 2;
        }

        public override void SetDefaults()
        {
            Projectile.netImportant = true; // This ensures that the projectile is synced when other players join the world.
            Projectile.width = 64; // The width of your projectile
            Projectile.height = 8; // The height of your projectile
            Projectile.friendly = true; // Deals damage to enemies
            Projectile.penetrate = -1; // Infinite pierce
            Projectile.DamageType = DamageClass.Melee; // Deals melee damage
            Projectile.usesLocalNPCImmunity = true; // Used for hit cooldown changes in the AI hook
            Projectile.localNPCHitCooldown = 10; // This facilitates custom hit cooldown logic

            // Vanilla flails all use aiStyle 15, but the code isn't customizable so an adaption of that aiStyle is used in the AI method
        }

        // This AI code was adapted from vanilla code: Terraria.Projectile.AI_015_Flails() 
        public override void AI()
        {
            Player player = Main.player[Projectile.owner];
            // Kill the projectile if the player dies or gets crowd controlled
            if (!player.active || player.dead || player.noItems || player.CCed || Vector2.Distance(Projectile.Center, player.Center) > 900f)
            {
                Projectile.Kill();
                return;
            }
            if (Main.myPlayer == Projectile.owner && Main.mapFullscreen)
            {
                Projectile.Kill();
                return;
            }

            // This line adds lighting (for a future update)
            // Lighting.AddLight(Projectile.position, 0.25f, 0f, 0.5f);

            Vector2 mountedCenter = player.Center;
            float hitSpeed = 10f; // How fast the projectile can move
            float maxHitLength = 32f; // How far the projectile will travel before returning
            float returnAcceleration = 3f; // How quickly the projectile will accelerate back towards the player
            float maxReturnSpeed = 10f; // The max speed the projectile will have while retruning
            float forcedReturnAcceleration = 6f; // How quickly the projectile will accelerate back towards the player while being forced to return
            float maxForcedReturnSpeed = 15f; // The max speed the projectile will have while being forced to return

            int defaultHitCooldown = 10; // How often your flail hits when resting on the ground, or retracting
            int spinHitCooldown = 20; // How often your flail hits when spinning
            int movingHitCooldown = 10; // How often your flail hits when moving
            int hitTimeLimit = 30; // How much time the projectile can go before returning
            int ricochetTimeLimit = 2;

            // Scaling these speeds and accelerations by the player's melee speed makes the weapon more responsive if the player bosts it or general weapon speed
            float meleeSpeedMultiplier = player.GetTotalAttackSpeed(DamageClass.Melee);
            hitSpeed *= meleeSpeedMultiplier;
            returnAcceleration *= meleeSpeedMultiplier;
            maxReturnSpeed *= meleeSpeedMultiplier;
            forcedReturnAcceleration *= meleeSpeedMultiplier;
            maxForcedReturnSpeed *= meleeSpeedMultiplier;
            float hitRange = hitSpeed * hitTimeLimit;
            float maxDroppedRange = hitRange + 160f;

            Projectile.localNPCHitCooldown = defaultHitCooldown;

            switch (CurrAIState)
            {
                case AIState.Spinning:
                    {
                        if (Projectile.owner == Main.myPlayer)
                        {
                            Vector2 unitVectorTowardsMouse = mountedCenter.DirectionTo(Main.MouseWorld).SafeNormalize(Vector2.UnitX * player.direction);
                            player.ChangeDir((unitVectorTowardsMouse.X > 0f).ToDirectionInt());
                            if (!player.channel) // If the player releases then change to moving forward mode
                            {
                                CurrAIState = AIState.Hit;
                                StateTimer = 0f;
                                Projectile.velocity = unitVectorTowardsMouse * hitSpeed + player.velocity;
                                Projectile.Center = mountedCenter;
                                Projectile.netUpdate = true;
                                Projectile.ResetLocalNPCHitImmunity();
                                Projectile.localNPCHitCooldown = movingHitCooldown;
                                break;
                            }
                        }
                        SpinningStateTimer += 1f;
                        Projectile.Center = mountedCenter;
                        Projectile.rotation += Projectile.velocity.X / 47.5f;
                        Projectile.localNPCHitCooldown = spinHitCooldown; // set the hit speed to the spinning hit speed
                        break;
                    }
                case AIState.Hit:
                    {
                        bool shouldSwitchToReturn = StateTimer++ >= hitTimeLimit;
                        shouldSwitchToReturn |= Projectile.Distance(mountedCenter) >= maxHitLength;

                        if (player.controlUseItem) // If the player clicks, transition to the Drop state
                        {
                            CurrAIState = AIState.Drop;
                            StateTimer = 0;
                            Projectile.netUpdate = true;
                            Projectile.velocity *= 0.2f;
                        }

                        if (shouldSwitchToReturn)
                        {
                            CurrAIState = AIState.Return;
                            StateTimer = 0;
                            Projectile.netUpdate = true;
                            Projectile.velocity *= 0.3f;
                        }
                        player.ChangeDir((player.Center.X < Projectile.Center.X).ToDirectionInt());
                        Projectile.localNPCHitCooldown = movingHitCooldown;
                        break;
                    }
                case AIState.Return:
                    {
                        Vector2 unitVectorTowardsPlayer = Projectile.DirectionTo(mountedCenter).SafeNormalize(Vector2.Zero);
                        if (Projectile.Distance(mountedCenter) <= maxReturnSpeed)
                        {
                            Projectile.Kill(); // Kill the projectile once it is close enough to the player
                            return;
                        }
                        if (player.controlUseItem) // If the player clicks, transition to the Dropping state
                        {
                            CurrAIState = AIState.Drop;
                            StateTimer = 0f;
                            Projectile.netUpdate = true;
                            Projectile.velocity *= 0.2f;
                        }
                        else
                        {
                            Projectile.velocity *= 0.98f;
                            Projectile.velocity = Projectile.velocity.MoveTowards(unitVectorTowardsPlayer * maxReturnSpeed, returnAcceleration);
                            player.ChangeDir((player.Center.X < Projectile.Center.X).ToDirectionInt());
                        }
                        break;
                    }
                case AIState.ForcedReturn:
                    {
                        Projectile.tileCollide = false;
                        Vector2 unitVectorTowardsPlayer = Projectile.DirectionTo(mountedCenter).SafeNormalize(Vector2.Zero);
                        if (Projectile.Distance(mountedCenter) <= maxForcedReturnSpeed)
                        {
                            Projectile.Kill(); // Kill the projectile once it is close enough to the player
                            return;
                        }
                        Projectile.velocity *= 0.98f;
                        Projectile.velocity = Projectile.velocity.MoveTowards(unitVectorTowardsPlayer * maxForcedReturnSpeed, forcedReturnAcceleration);
                        Vector2 target = Projectile.Center + Projectile.velocity;
                        Vector2 value = mountedCenter.DirectionFrom(target).SafeNormalize(Vector2.Zero);
                        if (Vector2.Dot(unitVectorTowardsPlayer, value) < 0f)
                        {
                            Projectile.Kill(); // Kill the projectile if it will pass the player
                            return;
                        }
                        player.ChangeDir((player.Center.X < Projectile.Center.X).ToDirectionInt());
                        break;
                    }

                case AIState.Ricochet:
                    {
                        if (StateTimer++ >= ricochetTimeLimit)
                        {
                            CurrAIState = AIState.Drop;
                            StateTimer = 0f;
                            Projectile.netUpdate = true;
                        }
                        else
                        {
                            Projectile.localNPCHitCooldown = movingHitCooldown;
                            Projectile.velocity.Y += 0.6f;
                            Projectile.velocity.X *= 0.95f;
                            player.ChangeDir((player.Center.X < Projectile.Center.X).ToDirectionInt());
                        }
                        break;
                    }
                case AIState.Drop:
                    {
                        if (!player.controlUseItem || Projectile.Distance(mountedCenter) > maxDroppedRange)
                        {
                            CurrAIState = AIState.ForcedReturn;
                            StateTimer = 0f;
                            Projectile.netUpdate = true;
                        }
                        else
                        {
                            Projectile.velocity.Y += 0.8f;
                            Projectile.velocity.X *= 0.95f;
                            player.ChangeDir((player.Center.X < Projectile.Center.X).ToDirectionInt());
                        }
                        break;
                    }
            }

            Projectile.timeLeft = 2; // Makes sure the flail doesn't die (good when the flail is resting on the ground)
            player.heldProj = Projectile.whoAmI;
            player.SetDummyItemTime(2); //Add a delay so the player can't button mash the flail
            player.itemRotation = Projectile.DirectionFrom(mountedCenter).ToRotation();
            if (Projectile.Center.X < mountedCenter.X)
            {
                player.itemRotation += (float)Math.PI;
            }
            player.itemRotation = MathHelper.WrapAngle(player.itemRotation);
        }

        public override bool OnTileCollide(Vector2 oldVelocity)
        {
            int impactIntensity = 0;
            Vector2 velocity = Projectile.velocity;
            float bounceFactor = 0.4f;
            if (CurrAIState == AIState.Hit || CurrAIState == AIState.Ricochet)
            {
                bounceFactor *= 1f;
            }
            if (CurrAIState == AIState.Drop)
            {
                bounceFactor = 0f;
            }

            if (oldVelocity.X != Projectile.velocity.X)
            {
                if (Math.Abs(oldVelocity.X) > 4f)
                {
                    impactIntensity = 1;
                }

                Projectile.velocity.X = (0f - oldVelocity.X) * bounceFactor;
                CollisionCounter += 1f;
            }

            if (oldVelocity.Y != Projectile.velocity.Y)
            {
                if (Math.Abs(oldVelocity.Y) > 4f)
                {
                    impactIntensity = 1;
                }

                Projectile.velocity.Y = (0f - oldVelocity.Y) * bounceFactor;
                CollisionCounter += 1f;
            }

            // Here the tiles spawn dust indicating they've been hit
            if (impactIntensity > 0)
            {
                Projectile.netUpdate = true;
                for (int i = 0; i < impactIntensity; i++)
                {
                    Collision.HitTiles(Projectile.position, velocity, Projectile.width, Projectile.height);
                }

                SoundEngine.PlaySound(SoundID.Dig, Projectile.position);
            }

            // Force retraction if stuck on tiles while retracting
            if (CurrAIState != AIState.Spinning && CurrAIState != AIState.Ricochet && CurrAIState != AIState.Drop && CollisionCounter >= 2f)
            {
                CurrAIState = AIState.ForcedReturn;
                Projectile.netUpdate = true;
            }

            return false;
        }

        public override bool? CanDamage()
        {
            // Flails in spin mode won't damage enemies within the first 12 ticks. Visually this delays the first hit until the player swings the flail around for a full spin before damaging anything.
            if (CurrAIState == AIState.Spinning && SpinningStateTimer <= 12f)
            {
                return false;
            }
            return base.CanDamage();
        }

        public override void ModifyDamageScaling(ref float damageScale)
        {
            // Flails do 20% more damage while spinning
            if (CurrAIState == AIState.Spinning)
            {
                damageScale *= 1.2f;
            }
            // Flails do 100% more damage while launched or retracting. This is the damage the item tooltip for flails aim to match, as this is the most common mode of attack. This is why the item has ItemID.Sets.ToolTipDamageMultiplier[Type] = 2f;
            else if (CurrAIState == AIState.Hit || CurrAIState == AIState.Return)
            {
                damageScale *= 2f;
            }
        }

        public override void ModifyHitNPC(NPC target, ref int damage, ref float knockback, ref bool crit, ref int hitDirection)
        {
            // Flails do a few custom things, you'll want to keep these to have the same feel as vanilla flails.

            // The hitDirection is always set to hit away from the player, even if the flail damages the npc while returning
            hitDirection = (Main.player[Projectile.owner].Center.X < target.Center.X).ToDirectionInt();

            // Knockback is only 25% as powerful when in spin mode
            if (CurrAIState == AIState.Spinning)
            {
                knockback *= 0.25f;
            }

            base.ModifyHitNPC(target, ref damage, ref knockback, ref crit, ref hitDirection);
        }

        // PreDraw is used to draw a purple light trail and motion blur before the projectile is drawn normally
        public override bool PreDraw(ref Color lightColor)
        {
            // Add a motion trail when moving forward, like most flails do (don't add trail if already hit a tile)
            if (CurrAIState == AIState.Hit)
            {
                Texture2D projectileTexture = TextureAssets.Projectile[Projectile.type].Value;
                Vector2 drawOrigin = new(projectileTexture.Width * 0.5f, Projectile.height * 0.5f);
                SpriteEffects spriteEffects = Projectile.spriteDirection == 1 ? SpriteEffects.None : SpriteEffects.FlipHorizontally;
                for (int k = 0; k < Projectile.oldPos.Length && k < StateTimer; k++)
                {
                    Vector2 drawPos = Projectile.oldPos[k] - Main.screenPosition + drawOrigin + new Vector2(0f, Projectile.gfxOffY);
                    Color color = Projectile.GetAlpha(lightColor) * ((float)(Projectile.oldPos.Length - k) / (float)Projectile.oldPos.Length);
                    Main.spriteBatch.Draw(projectileTexture, drawPos, null, color, Projectile.rotation, drawOrigin, Projectile.scale - k / (float)Projectile.oldPos.Length / 3, spriteEffects, 0f);
                }
            }
            return true;
        }
    }
}