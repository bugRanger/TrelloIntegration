﻿namespace TrelloIntegration.Services.Trello.Tasks
{
    using System;
    using TrelloIntegration.Common.Tasks;

    using Manatee.Trello;

    class UpdateFieldTask : TaskItem<TrelloService, string>
    {
        public string BoardId { get; }

        public string Id { get; }

        public string Name { get; }

        public CustomFieldType Type { get; }

        public IDropDownOption[] Options { get; }

        public UpdateFieldTask(string boardId, string name, string id = null, CustomFieldType type = CustomFieldType.Unknown, 
            IDropDownOption[] options = null, Action<string> callback = null) : base(callback)
        {
            BoardId = boardId;
            Id = id;
            Name = name;
            Type = type;
            Options = options;
        }

        protected override string HandleImpl(TrelloService service) 
        {
            return service.Handle(this);
        }
    }
}