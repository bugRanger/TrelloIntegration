﻿namespace TrelloIntegration.Services.Trello.Tasks
{
    using System;

    class UpdateListTask : Common.TaskItem<TrelloService, bool>
    {
        public string BoardId { get; }

        public string[] Lists { get; }

        public UpdateListTask(string boardId, string[] lists, Action<bool> callback = null) : base(callback)
        {
            BoardId = boardId;
            Lists = lists;
        }

        protected override bool HandleImpl(TrelloService service) 
        {
            return service.Handle(this);
        }
    }
}