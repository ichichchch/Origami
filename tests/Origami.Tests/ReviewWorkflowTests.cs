namespace Origami.Tests;

/// <summary>
/// 待審稿流程：Author 角色可撰寫自己的內容並提交待審但不得發布、編輯者審核發布或退回、
/// Pending 內容不公開且不進嵌入管道、作者彙整依署名代稱過濾。
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReviewWorkflowTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private async Task<(UserSummary User, string DisplayName)> CreateAuthorAsync(string name, string displayName)
    {
        var user = await CreateMemberAsync(SiteRole.Author, name: name);
        using var admin = await App.AdministratorAsync();
        var member = (await admin.Sites.ListMembersAsync()).Single(x => x.UserId == user.Id);
        await admin.Sites.SetMemberAsync(new MemberEditInput
        {
            UserName = member.UserName, Role = member.Role, DisplayName = displayName, Version = member.Version
        });
        return (user, displayName);
    }

    [PostgresFact]
    public async Task Authors_submit_their_own_work_but_cannot_publish_or_edit_others()
    {
        var (author, _) = await CreateAuthorAsync("author-a", "作者甲");
        using var actor = await App.UserAsync(author.Id);

        var draft = await actor.Content.SaveAsync(NewContent("作者的草稿"));
        Assert.Equal(ContentStatus.Draft, draft.Status);

        // 提交待審成功；作者不可以自行發布。
        var submitted = await actor.Content.SaveAsync(new ContentEdit
        {
            Id = draft.Id, Kind = draft.Kind, Title = draft.Title, Slug = draft.Slug,
            Markdown = draft.Markdown, Status = ContentStatus.Pending, Version = draft.Version
        });
        Assert.Equal(ContentStatus.Pending, submitted.Status);

        var publishAttempt = new ContentEdit
        {
            Id = submitted.Id, Kind = submitted.Kind, Title = submitted.Title, Slug = submitted.Slug,
            Markdown = submitted.Markdown, Status = ContentStatus.Published, Version = submitted.Version
        };
        await Assert.ThrowsAsync<CmsException>(() => actor.Content.SaveAsync(publishAttempt));

        // 作者不能編輯他人建立的內容。
        using var admin = await App.AdministratorAsync();
        var others = await admin.Content.SaveAsync(NewContent("管理者的草稿"));
        await Assert.ThrowsAsync<AccessDeniedException>(() => actor.Content.SaveAsync(new ContentEdit
        {
            Id = others.Id, Kind = others.Kind, Title = others.Title, Slug = others.Slug,
            Markdown = "作者的改動", Status = ContentStatus.Draft, Version = others.Version
        }));
    }

    [PostgresFact]
    public async Task Editors_publish_or_decline_pending_work()
    {
        var (author, _) = await CreateAuthorAsync("author-b", "作者乙");
        using var authorActor = await App.UserAsync(author.Id);
        var draft = await authorActor.Content.SaveAsync(NewContent("待審的文章"));
        var submitted = await authorActor.Content.SaveAsync(new ContentEdit
        {
            Id = draft.Id, Kind = draft.Kind, Title = draft.Title, Slug = draft.Slug,
            Markdown = draft.Markdown, Status = ContentStatus.Pending, Version = draft.Version
        });

        // Pending 不公開、不進嵌入管道。
        Assert.Null(await authorActor.Content.GetPublishedAsync(ContentKind.Post, submitted.Slug!));
        await using (var db = await App.CreateContextAsync())
        {
            Assert.False(await db.EmbeddingJobs.AsNoTracking().AnyAsync(x => x.ContentId == submitted.Id));
        }

        // 編輯者審核通過並發布。
        var editorUser = await CreateMemberAsync(SiteRole.Editor, name: "review-editor");
        using var editor = await App.UserAsync(editorUser.Id);
        var editorAccess = await editor.Access.GetCurrentAccessAsync();
        Assert.False(editorAccess.IsNetworkAdmin);
        Assert.Equal(SiteRole.Editor, editorAccess.Role);
        var published = await editor.Content.SaveAsync(new ContentEdit
        {
            Id = submitted.Id, Kind = submitted.Kind, Title = submitted.Title, Slug = submitted.Slug,
            Markdown = submitted.Markdown, Status = ContentStatus.Published, Version = submitted.Version
        });
        Assert.NotNull(published.PublishedAt);

        // 另一篇：編輯者退回草稿（快速編輯路徑）。
        var second = await authorActor.Content.SaveAsync(NewContent("被退回的文章"));
        var secondSubmitted = await authorActor.Content.SaveAsync(new ContentEdit
        {
            Id = second.Id, Kind = second.Kind, Title = second.Title, Slug = second.Slug,
            Markdown = second.Markdown, Status = ContentStatus.Pending, Version = second.Version
        });
        await editor.Content.QuickEditAsync(secondSubmitted.Id!.Value, secondSubmitted.Title, secondSubmitted.Slug,
            ContentStatus.Draft, secondSubmitted.Version);
        await using var db2 = await App.CreateContextAsync();
        Assert.Equal(ContentStatus.Draft,
            (await db2.Contents.AsNoTracking().SingleAsync(x => x.Id == secondSubmitted.Id)).Status);
    }

    [PostgresFact]
    public async Task Author_archive_lists_only_that_authors_published_posts()
    {
        var (authorA, _) = await CreateAuthorAsync("author-c", "作者丙");
        var (authorB, _) = await CreateAuthorAsync("author-d", "作者丁");
        using var admin = await App.AdministratorAsync();
        using (var actorA = await App.UserAsync(authorA.Id))
        {
            var a = await actorA.Content.SaveAsync(NewContent("丙的文章", status: ContentStatus.Draft));
            await actorA.Content.SaveAsync(new ContentEdit
            {
                Id = a.Id, Kind = a.Kind, Title = a.Title, Slug = a.Slug,
                Markdown = a.Markdown, Status = ContentStatus.Pending, Version = a.Version
            });
        }
        using (var actorB = await App.UserAsync(authorB.Id))
        {
            var b = await actorB.Content.SaveAsync(NewContent("丁的文章", status: ContentStatus.Draft));
            await actorB.Content.SaveAsync(new ContentEdit
            {
                Id = b.Id, Kind = b.Kind, Title = b.Title, Slug = b.Slug,
                Markdown = b.Markdown, Status = ContentStatus.Pending, Version = b.Version
            });
        }
        // 編輯者分別發布兩位作者的文章。
        await using var db = await App.CreateContextAsync();
        foreach (var id in await db.Contents.Where(x => x.Status == ContentStatus.Pending).Select(x => x.Id).ToListAsync())
        {
            var edit = await admin.Content.GetEditAsync(id);
            await admin.Content.SaveAsync(new ContentEdit
            {
                Id = edit.Id, Kind = edit.Kind, Title = edit.Title, Slug = edit.Slug, Markdown = edit.Markdown,
                Status = ContentStatus.Published, Version = edit.Version
            });
        }

        var archiveA = await admin.Content.ListPublishedAsync(author: "作者丙");
        Assert.Single(archiveA.Items);
        Assert.Equal("丙的文章", archiveA.Items[0].Title);

        var archiveB = await admin.Content.ListPublishedAsync(author: "作者丁");
        Assert.Single(archiveB.Items);
        Assert.Equal("丁的文章", archiveB.Items[0].Title);

        // 計數包含待審狀態。
        var counts = await admin.Content.CountAdminAsync(ContentKind.Post);
        Assert.Equal(0, counts.Pending);
        Assert.Equal(2, counts.Published);
    }

    [PostgresFact]
    public async Task Authors_cannot_create_scheduled_drafts_or_pending_work()
    {
        var author = await CreateMemberAsync(SiteRole.Author);
        using var actor = await App.UserAsync(author.Id);
        var before = await SnapshotAsync();
        foreach (var kind in new[] { ContentKind.Post, ContentKind.Page })
        foreach (var status in new[] { ContentStatus.Draft, ContentStatus.Pending })
        {
            var input = NewContent($"Scheduled {kind} {status}", kind, status);
            input.PublishAt = DateTimeOffset.UtcNow.AddHours(1);
            await Assert.ThrowsAsync<CmsException>(() => actor.Content.CreateAsync(kind, input));
            Assert.Equal(before, await SnapshotAsync());
        }
    }

    [PostgresFact]
    public async Task Authors_cannot_add_schedules_when_updating_drafts_or_pending_work()
    {
        var author = await CreateMemberAsync(SiteRole.Author);
        using var actor = await App.UserAsync(author.Id);
        foreach (var kind in new[] { ContentKind.Post, ContentKind.Page })
        foreach (var originalStatus in new[] { ContentStatus.Draft, ContentStatus.Pending })
        {
            var saved = await actor.Content.SaveAsync(NewContent($"Original {kind} {originalStatus}", kind, originalStatus));
            var before = await SnapshotAsync();
            foreach (var targetStatus in new[] { ContentStatus.Draft, ContentStatus.Pending })
            {
                var input = await actor.Content.GetEditAsync(saved.Id!.Value);
                input.Title = "Unauthorized schedule";
                input.Markdown = "Changed body";
                input.Status = targetStatus;
                input.PublishAt = DateTimeOffset.UtcNow.AddHours(1);
                await Assert.ThrowsAsync<CmsException>(() => actor.Content.UpdateAsync(input));
                Assert.Equal(before, await SnapshotAsync());
            }
        }
    }

    [PostgresFact]
    public async Task Authors_cannot_change_or_clear_an_editor_approved_schedule()
    {
        var author = await CreateMemberAsync(SiteRole.Author);
        var editorUser = await CreateMemberAsync(SiteRole.Editor, name: "schedule-editor");
        using var actor = await App.UserAsync(author.Id);
        using var editor = await App.UserAsync(editorUser.Id);
        foreach (var kind in new[] { ContentKind.Post, ContentKind.Page })
        {
            var saved = await actor.Content.SaveAsync(NewContent($"Approved {kind}", kind, ContentStatus.Pending));
            var approved = await editor.Content.GetEditAsync(saved.Id!.Value);
            approved.Status = ContentStatus.Draft;
            approved.PublishAt = DateTimeOffset.UtcNow.AddHours(1);
            await editor.Content.UpdateAsync(approved);
            var before = await SnapshotAsync();
            foreach (var targetStatus in new[] { ContentStatus.Draft, ContentStatus.Pending })
            foreach (var schedule in new DateTimeOffset?[] { null, approved.PublishAt, approved.PublishAt!.Value.AddHours(1) })
            {
                var input = await actor.Content.GetEditAsync(saved.Id.Value);
                input.Title = "Changed after approval";
                input.Markdown = "Unreviewed body";
                input.Status = targetStatus;
                input.PublishAt = schedule;
                await Assert.ThrowsAsync<AccessDeniedException>(() => actor.Content.UpdateAsync(input));
                Assert.Equal(before, await SnapshotAsync());
            }
        }
    }

    [PostgresFact]
    public async Task Authors_cannot_rewrite_published_content_as_draft_or_pending()
    {
        var author = await CreateMemberAsync(SiteRole.Author);
        var editorUser = await CreateMemberAsync(SiteRole.Editor, name: "publisher");
        using var actor = await App.UserAsync(author.Id);
        using var editor = await App.UserAsync(editorUser.Id);
        foreach (var kind in new[] { ContentKind.Post, ContentKind.Page })
        {
            var saved = await actor.Content.SaveAsync(NewContent($"Published {kind}", kind, ContentStatus.Pending));
            var approved = await editor.Content.GetEditAsync(saved.Id!.Value);
            approved.Status = ContentStatus.Published;
            await editor.Content.UpdateAsync(approved);
            await using (var db = await App.CreateContextAsync())
            {
                db.ContentEmbeddings.Add(new ContentEmbedding
                {
                    ContentId = saved.Id.Value, ChunkIndex = 0, ChunkText = saved.Markdown!,
                    Embedding = new Vector(FakeEmbeddingGateway.Basis(0))
                });
                await db.SaveChangesAsync();
                Assert.True(await db.EmbeddingJobs.AnyAsync(x => x.ContentId == saved.Id));
            }
            var before = await SnapshotAsync();
            foreach (var targetStatus in new[] { ContentStatus.Draft, ContentStatus.Pending })
            {
                var input = await actor.Content.GetEditAsync(saved.Id.Value);
                input.Title = "Unapproved change";
                input.Slug = "unapproved-" + kind.ToString().ToLowerInvariant();
                input.Markdown = "Unapproved body";
                input.Status = targetStatus;
                await Assert.ThrowsAsync<AccessDeniedException>(() => actor.Content.UpdateAsync(input));
                Assert.Equal(before, await SnapshotAsync());
            }
        }
    }

    [PostgresFact]
    public async Task Authors_can_rotate_and_revoke_previews_for_their_drafts_and_pending_work()
    {
        var author = await CreateMemberAsync(SiteRole.Author);
        using var actor = await App.UserAsync(author.Id);
        using var visitor = App.Anonymous();
        foreach (var kind in new[] { ContentKind.Post, ContentKind.Page })
        foreach (var status in new[] { ContentStatus.Draft, ContentStatus.Pending })
        {
            var saved = await actor.Content.SaveAsync(NewContent($"Preview {kind} {status}", kind, status));
            var token = await actor.Content.CyclePreviewTokenAsync(saved.Id!.Value);
            var preview = await visitor.Content.GetPreviewAsync(token);
            Assert.NotNull(preview);
            Assert.Equal(saved.Markdown, preview.Markdown);
            Assert.Null(await visitor.Content.GetPublishedAsync(kind, saved.Slug!));
            Assert.Null(await visitor.Content.ResolvePublishedPreviewAsync(token));
            var rotated = await actor.Content.CyclePreviewTokenAsync(saved.Id.Value);
            Assert.NotEqual(token, rotated);
            Assert.Null(await visitor.Content.GetPreviewAsync(token));
            Assert.NotNull(await visitor.Content.GetPreviewAsync(rotated));
            await actor.Content.RevokePreviewTokenAsync(saved.Id.Value);
            Assert.Null(await visitor.Content.GetPreviewAsync(rotated));
            var edit = await actor.Content.GetEditAsync(saved.Id.Value);
            Assert.Equal(saved.Version, edit.Version);
            Assert.Equal(status, edit.Status);
        }
    }

    [PostgresFact]
    public async Task Authors_cannot_change_other_authors_or_noneditable_previews()
    {
        var author = await CreateMemberAsync(SiteRole.Author);
        var other = await CreateMemberAsync(SiteRole.Author, name: "other-author");
        var editorUser = await CreateMemberAsync(SiteRole.Editor, name: "preview-editor");
        using var actor = await App.UserAsync(author.Id);
        using var otherActor = await App.UserAsync(other.Id);
        using var editor = await App.UserAsync(editorUser.Id);
        using var visitor = App.Anonymous();
        var othersDraft = await otherActor.Content.SaveAsync(NewContent("Others draft"));
        var othersPending = await otherActor.Content.SaveAsync(NewContent("Others pending", status: ContentStatus.Pending));
        var published = await actor.Content.SaveAsync(NewContent("Published preview"));
        var publishedToken = await actor.Content.CyclePreviewTokenAsync(published.Id!.Value);
        published.Status = ContentStatus.Published;
        await editor.Content.UpdateAsync(published);
        Assert.Null(await visitor.Content.GetPreviewAsync(publishedToken));
        Assert.Equal(published.Slug, (await visitor.Content.ResolvePublishedPreviewAsync(publishedToken))!.Slug);
        var scheduled = await actor.Content.SaveAsync(NewContent("Scheduled preview"));
        await actor.Content.CyclePreviewTokenAsync(scheduled.Id!.Value);
        scheduled.PublishAt = DateTimeOffset.UtcNow.AddHours(1);
        await editor.Content.UpdateAsync(scheduled);
        var trashed = await actor.Content.SaveAsync(NewContent("Trashed preview"));
        var trashedToken = await actor.Content.CyclePreviewTokenAsync(trashed.Id!.Value);
        await editor.Content.TrashAsync(trashed.Id.Value, trashed.Version, true);
        Assert.Null(await visitor.Content.GetPreviewAsync(trashedToken));
        Assert.Null(await visitor.Content.ResolvePublishedPreviewAsync(trashedToken));
        foreach (var saved in new[] { othersDraft, othersPending, published, scheduled, trashed })
        {
            var before = await SnapshotAsync();
            await Assert.ThrowsAsync<AccessDeniedException>(() => actor.Content.CyclePreviewTokenAsync(saved.Id!.Value));
            await Assert.ThrowsAsync<AccessDeniedException>(() => actor.Content.RevokePreviewTokenAsync(saved.Id!.Value));
            Assert.Equal(before, await SnapshotAsync());
        }
        var editorToken = await editor.Content.CyclePreviewTokenAsync(published.Id.Value);
        Assert.Null(await visitor.Content.GetPreviewAsync(editorToken));
        Assert.NotNull(await visitor.Content.ResolvePublishedPreviewAsync(editorToken));
        await editor.Content.RevokePreviewTokenAsync(published.Id.Value);
        Assert.Null(await visitor.Content.ResolvePublishedPreviewAsync(editorToken));
    }

    [PostgresFact]
    public async Task Authors_cannot_read_or_change_their_content_through_another_site()
    {
        var site = await CreateSiteAsync();
        var author = await CreateMemberAsync(SiteRole.Author);
        using var siteAdmin = await App.AdministratorAsync(site.Id);
        await siteAdmin.Sites.SetMemberAsync(new MemberEditInput { UserName = author.UserName, Role = SiteRole.Author });
        using var local = await App.UserAsync(author.Id);
        using var remote = await App.UserAsync(author.Id, site.Id);
        var saved = await remote.Content.SaveAsync(NewContent("Remote author's draft"));
        var id = saved.Id!.Value;
        var token = await remote.Content.CyclePreviewTokenAsync(id);
        await remote.Content.TouchEditingAsync(id);
        var before = await SnapshotAsync(site.Id);
        var localBefore = await SnapshotAsync();
        await Assert.ThrowsAsync<CmsException>(() => local.Content.UpdateAsync(saved));
        await Assert.ThrowsAsync<CmsException>(() => local.Content.TouchEditingAsync(id, takeOver: true));
        await local.Content.ReleaseEditingAsync(id);
        await Assert.ThrowsAsync<CmsException>(() => local.Content.CyclePreviewTokenAsync(id));
        await Assert.ThrowsAsync<CmsException>(() => local.Content.RevokePreviewTokenAsync(id));
        Assert.Null(await local.Content.GetPreviewAsync(token));
        Assert.Null(await local.Content.ResolvePublishedPreviewAsync(token));
        Assert.Equal(before, await SnapshotAsync(site.Id));
        Assert.Equal(localBefore, await SnapshotAsync());
    }

    [PostgresFact]
    public async Task Author_access_is_revalidated_and_does_not_grant_editor_operations()
    {
        var author = await CreateMemberAsync(SiteRole.Author);
        using var actor = await App.UserAsync(author.Id);
        using var admin = await App.AdministratorAsync();
        var saved = await actor.Content.SaveAsync(NewContent("Role revalidation"));
        var id = saved.Id!.Value;
        await actor.Content.TouchEditingAsync(id);
        await actor.Content.CyclePreviewTokenAsync(id);
        var before = await SnapshotAsync();
        await Assert.ThrowsAsync<AccessDeniedException>(() => actor.Content.QuickEditAsync(id, saved.Title, saved.Slug, ContentStatus.Published, saved.Version));
        await Assert.ThrowsAsync<AccessDeniedException>(() => actor.Content.TrashAsync(id, saved.Version, true));
        await Assert.ThrowsAsync<AccessDeniedException>(() => actor.Content.DeleteAsync(id, saved.Version, true));
        var member = (await admin.Sites.ListMembersAsync()).Single(x => x.UserId == author.Id);
        await admin.Sites.SetMemberAsync(new MemberEditInput
        {
            UserName = member.UserName, Role = SiteRole.Reader, Version = member.Version
        });
        await Assert.ThrowsAsync<AccessDeniedException>(() => actor.Content.UpdateAsync(saved));
        await Assert.ThrowsAsync<AccessDeniedException>(() => actor.Content.TouchEditingAsync(id));
        await Assert.ThrowsAsync<AccessDeniedException>(() => actor.Content.ReleaseEditingAsync(id));
        await Assert.ThrowsAsync<AccessDeniedException>(() => actor.Content.CyclePreviewTokenAsync(id));
        await Assert.ThrowsAsync<AccessDeniedException>(() => actor.Content.RevokePreviewTokenAsync(id));
        Assert.Equal(before, await SnapshotAsync());
    }

    private async Task<string> SnapshotAsync(Guid? siteId = null)
    {
        await using var db = await App.CreateContextAsync(siteId);
        var contents = await db.Contents.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var revisions = await db.ContentRevisions.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var jobs = await db.EmbeddingJobs.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var embeddings = await db.ContentEmbeddings.AsNoTracking().OrderBy(x => x.ContentId).ThenBy(x => x.ChunkIndex).ToListAsync();
        var redirects = await db.SlugRedirects.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        return JsonSerializer.Serialize(new
        {
            Contents = contents, Revisions = revisions, Jobs = jobs, Redirects = redirects,
            Embeddings = embeddings.Select(x => new { x.SiteId, x.ContentId, x.ChunkIndex, x.ChunkText, Values = x.Embedding.ToArray() })
        });
    }
}
