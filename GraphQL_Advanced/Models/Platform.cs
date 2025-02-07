﻿using HotChocolate;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace GraphQL_Advanced.Models
{
    //[GraphQLDescription("Represents any software or service that service that has a command line interface")]
    public class Platform
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Name { get; set; }

        //[GraphQLDescription("Represents a purchased, valid license for the platform")]
        public string LicenseKey { get; set; }

        public ICollection<Command> Commands { get; set; } = new List<Command>(); 
    }
}